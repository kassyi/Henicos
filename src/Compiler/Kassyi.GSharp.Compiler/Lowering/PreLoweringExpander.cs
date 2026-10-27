namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands macro statements before the state-machine lowering passes run: a <c>Gpu.ForUnroll</c> that contains phase
/// barriers into a standard <c>for</c> loop, and a <c>Gpu.SyncGrid(body)</c> into the statements of its body with a
/// <c>Gpu.SyncGrid()</c> in each gap (<see cref="SyncGridBody"/>).
/// </summary>
/// <remarks>
/// The state machine splits only at barriers that are statements of the method (as Roslyn lifts every <c>await</c> to
/// the statement level before its state machine rewriter runs), so a body that runs between grid syncs must become
/// statements first (#626).
/// </remarks>
public sealed class PreLoweringExpander(SemanticModel? semanticModel = null) : CSharpSyntaxRewriter
{
    private int _expansionCounter;

    private static bool ContainsBarrier(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && BarrierHelper.IsPhaseBarrier(inv));

    /// <summary>Expands barrier-containing macro statements within <paramref name="block"/>.</summary>
    public static BlockSyntax Expand(BlockSyntax block, SemanticModel? semanticModel) =>
        new PreLoweringExpander(semanticModel).Expand(block);

    /// <summary>Expands barrier-containing macro statements within <paramref name="block"/>.</summary>
    public BlockSyntax Expand(BlockSyntax block) =>
        ContainsBarrier(block) ? (BlockSyntax)Visit(block) : block;

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (SyncGridBody.TryGetBody(node, semanticModel, out var syncGridBody))
        {
            // Top-down expansion: nested macros in the body (in loops, ForUnroll bodies) are expanded in turn
            return Visit(ExpandSyncGrid(node, syncGridBody));
        }

        if (node.Expression is not InvocationExpressionSyntax invocation ||
            !TryClassifyForUnroll(invocation, out var call))
        {
            return base.VisitExpressionStatement(node);
        }

        var args = invocation.ArgumentList.Arguments;
        if (args.Count <= call.LambdaArgumentIndex ||
            args[call.LambdaArgumentIndex].Expression is not LambdaExpressionSyntax lambdaExpr ||
            !ContainsBarrier(lambdaExpr.Body))
        {
            return base.VisitExpressionStatement(node);
        }

        if (GpuCodeConstraints.GetMacroLoopParameter(lambdaExpr, call.HasContext) is not { } loopParam ||
            loopParam.Identifier.ValueText.Length == 0)
        {
            return base.VisitExpressionStatement(node);
        }

        var offset = call.ArgumentOffset;
        var startExpr = args[offset].Expression.WithoutTrivia();
        var countExpr = args[offset + 1].Expression.WithoutTrivia();
        if (!TryBuildBoundExpression(startExpr, countExpr, out var boundExpr))
        {
            return base.VisitExpressionStatement(node);
        }

        ParameterSyntax? contextParam = null;
        ExpressionSyntax? contextArgExpr = null;
        if (call.HasContext && lambdaExpr is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenLambda)
        {
            contextParam = parenLambda.ParameterList.Parameters[0];
            contextArgExpr = args[0].Expression.WithoutTrivia();
        }

        var id = _expansionCounter++;
        var indexName = $"{GSharpInternalNames.Lowering.UnrollIndexPrefix}{id}";
        var annotations = ImmutableArray.CreateRange(
            node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
        );

        var substituter = new MacroLambdaSubstituter(
            semanticModel,
            loopParam,
            indexName,
            contextParam,
            contextArgExpr
        );
        var substitutedBody = substituter.Visit(lambdaExpr.Body) ?? lambdaExpr.Body;
        var bodyBlock = ToBodyBlock(substitutedBody, annotations);

        // for (int __gsharp_unroll_<id> = <startExpr>; __gsharp_unroll_<id> < <boundExpr>; __gsharp_unroll_<id>++) <bodyBlock>
        var indexDeclarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(indexName))
            .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr));
        var indexDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
            SyntaxFactory.SingletonSeparatedList(indexDeclarator)
        ).WithAdditionalAnnotations(annotations);

        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            boundExpr
        ).WithAdditionalAnnotations(annotations);

        var incrementor = SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.PostIncrementExpression,
            SyntaxFactory.IdentifierName(indexName)
        ).WithAdditionalAnnotations(annotations);

        var forStatement = SyntaxFactory.ForStatement(
            indexDeclaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            bodyBlock
        ).WithAdditionalAnnotations(annotations);

        // Top-down expansion: revisit the expanded loop so nested barrier-bearing constructs receive subsequent IDs
        return Visit(forStatement);
    }

    // { <statement 1> Gpu.SyncGrid(); <statement 2> ... }: a sync in each gap that SyncGridBody.IsSyncPoint places; a
    // nested body is a block of its own; a body that returns gets its done flag, labels and guards (SyncGridBody)
    private BlockSyntax ExpandSyncGrid(ExpressionStatementSyntax node, LambdaExpressionSyntax body)
    {
        var annotations = ImmutableArray.CreateRange(
            node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
        );
        var statements = SyncGridBody.Flatten(body, semanticModel);
        var bodyIds = new int[statements.BodyParents.Length];
        for (var i = 0; i < bodyIds.Length; i++)
        {
            bodyIds[i] = _expansionCounter++;
        }

        var index = 0;
        return ExpandBody(statements, bodyIndex: 0, bodyIds, ref index, annotations);
    }

    // The statements of body bodyIndex from statements.Elements[index] on, the bodies nested in it as blocks of their own;
    // returns with index at the first statement outside the body
    private BlockSyntax ExpandBody(
        SyncGridStatements statements,
        int bodyIndex,
        int[] bodyIds,
        ref int index,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var elements = statements.Elements;
        var head = ImmutableArray.CreateBuilder<StatementSyntax>();
        var rest = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (statements.BodyHasReturn(bodyIndex))
        {
            head.Add(CreateDoneFlag(DoneFlagName(bodyIds[bodyIndex])).WithAdditionalAnnotations(annotations));
        }

        while (index < elements.Length && statements.IsWithin(elements[index].BodyIndex, bodyIndex))
        {
            var element = elements[index];
            if (element.BodyIndex == bodyIndex)
            {
                ExpandStatement(statements, index, bodyIds, head, rest);
                index++;
            }
            else
            {
                var child = statements.ChildOnPath(element.BodyIndex, bodyIndex);
                rest.Add(ExpandBody(statements, child, bodyIds, ref index, annotations));
            }

            // The sync after a statement goes where the next statement is: after the blocks that end here, before the
            // ones that start with it
            if (index < elements.Length &&
                statements.IsWithin(elements[index].BodyIndex, bodyIndex) &&
                SyncGridBody.IsSyncPoint(elements, index - 1, semanticModel))
            {
                rest.Add(CreateGridSync(annotations));
            }
        }

        head.AddRange(rest);
        return SyntaxFactory.Block(head).WithAdditionalAnnotations(annotations);
    }

    // One statement of a body: its returns rewritten, the label after it when it returns, the guard of the bodies that
    // returned before it; a guarded declaration and the out variables of a guarded statement are declared in head and
    // assigned in the guard
    private void ExpandStatement(
        SyncGridStatements statements,
        int index,
        int[] bodyIds,
        ImmutableArray<StatementSyntax>.Builder head,
        ImmutableArray<StatementSyntax>.Builder rest)
    {
        var element = statements.Elements[index];
        var statement = ToStatement(element.Node);
        var bodyId = bodyIds[element.BodyIndex];
        var label = $"{GSharpInternalNames.Lowering.SyncGridNextLabelPrefix}{bodyId}_{index}";
        if (element.HasBodyReturn)
        {
            statement = SyncGridBody.RewriteReturns(statement, DoneFlagName(bodyId), label);
        }

        var guards = statements.GetGuardBodies(index, semanticModel);
        var elementAnnotations = statement.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        if (guards.IsEmpty)
        {
            rest.Add(statement);
        }
        else
        {
            var guarded = ImmutableArray.CreateBuilder<StatementSyntax>();
            if (statement is LocalDeclarationStatementSyntax declaration &&
                SyncGridBody.CanSplit(declaration, semanticModel))
            {
                SplitDeclaration(declaration, head, guarded);
            }
            else
            {
                guarded.Add(statement);
            }

            for (var i = 0; i < guarded.Count; i++)
            {
                guarded[i] = MoveOutVariables(guarded[i], head);
            }

            if (guarded.Count > 0)
            {
                rest.Add(CreateGuard(guards, bodyIds, guarded).WithAdditionalAnnotations(elementAnnotations));
            }
        }

        if (element.HasBodyReturn)
        {
            // <label>: ;
            // No original-span annotation, as for the labels of LoopRewriter and BranchRewriter: the label is keyed by its
            // name, not by the symbol of the statement it follows (a foreach variable, a label of the user)
            rest.Add(SyntaxFactory.LabeledStatement(label, SyntaxFactory.EmptyStatement()));
        }
    }

    // <type> <name>; for each declarator into head; <name> = <initializer>; for each initializer into assignments
    private void SplitDeclaration(
        LocalDeclarationStatementSyntax declaration,
        ImmutableArray<StatementSyntax>.Builder head,
        ImmutableArray<StatementSyntax>.Builder assignments)
    {
        var annotations = declaration.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        foreach (var declarator in declaration.Declaration.Variables)
        {
            // <type> <name>;
            var moved = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    GetDeclaredType(declaration.Declaration.Type, declarator),
                    SyntaxFactory.SingletonSeparatedList(declarator.WithInitializer(null).WithoutTrivia())
                )
            ).WithAdditionalAnnotations(annotations);
            head.Add(moved);
            if (declarator.Initializer is not { } initializer)
            {
                continue;
            }

            // The name carries the span of the declarator, so it binds to the local (LoweringIdentity.ForReference)
            var name = SyntaxFactory.IdentifierName(declarator.Identifier.WithoutTrivia())
                .WithAdditionalAnnotations(declarator.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            // <name> = <initializer>;
            assignments.Add(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        name,
                        initializer.Value.WithoutTrivia()
                    )
                ).WithAdditionalAnnotations(annotations)
            );
        }
    }

    // <type> <name>; into head for each out variable that statement declares into the body; out var <name> becomes
    // out <name>
    private StatementSyntax MoveOutVariables(StatementSyntax statement, ImmutableArray<StatementSyntax>.Builder head)
    {
        var variables = ImmutableArray.CreateBuilder<DeclarationExpressionSyntax>();
        SyncGridBody.AddMovedOutVariables(statement, variables);
        foreach (var variable in variables)
        {
            // Only single variable designations are collected (SyncGridBody.AddMovedOutVariables)
            var designation = (SingleVariableDesignationSyntax)variable.Designation;
            var type = variable.Type.IsVar && semanticModel?.GetDeclaredSymbol<ILocalSymbol>(designation) is { } local
                ? ToTypeSyntax(local.Type)
                : variable.Type.WithoutTrivia();
            // The declarator carries the span of the designation, so it declares the same local
            var declarator = SyntaxFactory.VariableDeclarator(designation.Identifier.WithoutTrivia())
                .WithAdditionalAnnotations(designation.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            // <type> <name>;
            head.Add(
                SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(type, SyntaxFactory.SingletonSeparatedList(declarator))
                ).WithAdditionalAnnotations(variable.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
            );
        }

        return SyncGridBody.RewriteOutVariables(statement, variables);
    }

    // var becomes the type of the local; without a model it stays var
    private TypeSyntax GetDeclaredType(TypeSyntax declared, VariableDeclaratorSyntax declarator) =>
        declared.IsVar && semanticModel?.GetDeclaredSymbol<ILocalSymbol>(declarator) is { } local
            ? ToTypeSyntax(local.Type)
            : declared.WithoutTrivia();

    // The C# keyword of a special type (int), otherwise the fully qualified name
    private static TypeSyntax ToTypeSyntax(ITypeSymbol type) =>
        SyntaxFactory.ParseTypeName(type.SpecialType.GetCsharpKeyword() ?? type.GetFullyQualifiedCsharpName());

    // if (!<flag 1> && !<flag 2> ...) { <statements> }
    private static IfStatementSyntax CreateGuard(
        ImmutableArray<int> guards,
        int[] bodyIds,
        ImmutableArray<StatementSyntax>.Builder statements)
    {
        ExpressionSyntax? condition = null;
        foreach (var guard in guards)
        {
            var notDone = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.IdentifierName(DoneFlagName(bodyIds[guard]))
            );
            condition = condition == null
                ? notDone
                : SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, condition, notDone);
        }

        return SyntaxFactory.IfStatement(
            condition ?? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression),
            SyntaxFactory.Block(statements)
        );
    }

    // bool <flag> = false;
    private static LocalDeclarationStatementSyntax CreateDoneFlag(string flag) =>
        SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(flag)
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)
                            )
                        )
                )
            )
        );

    private static string DoneFlagName(int bodyId) => $"{GSharpInternalNames.Lowering.SyncGridDonePrefix}{bodyId}";

    // Gpu.SyncGrid();
    // The statement carries the span of the Gpu.SyncGrid(body) it came from, for diagnostics; BarrierHelper and the state
    // machine recognize the call by its syntax.
    private static ExpressionStatementSyntax CreateGridSync(ImmutableArray<SyntaxAnnotation> annotations) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(TypeConstants.GpuType),
                    SyntaxFactory.IdentifierName(TypeConstants.GpuMacros.SyncGrid)
                )
            )
        ).WithAdditionalAnnotations(annotations);

    // An expression body of a lambda becomes <expression>; with the span of the expression
    private static StatementSyntax ToStatement(CSharpSyntaxNode node) =>
        node switch
        {
            StatementSyntax statement => statement,
            ExpressionSyntax expression => SyntaxFactory.ExpressionStatement(expression)
                .WithAdditionalAnnotations(expression.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)),
            _ => throw new InvalidOperationException($"Cannot expand a SyncGrid body element of kind '{node.Kind()}'.")
        };

    private bool TryClassifyForUnroll(InvocationExpressionSyntax invocation, out GpuMacroCall call)
    {
        if (semanticModel != null)
        {
            call = semanticModel.GetSymbol(invocation) is IMethodSymbol methodSymbol
                ? GpuMacroClassifier.Classify(methodSymbol)
                : default;
            return call.Kind == GpuMacroKind.ForUnroll;
        }

        call = default;
        var isForUnrollSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType },
                Name.Identifier.ValueText: TypeConstants.GpuMacros.ForUnroll
            } => true,
            IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuMacros.ForUnroll } => true,
            _ => false
        };
        if (!isForUnrollSyntax)
        {
            return false;
        }

        var argCount = invocation.ArgumentList.Arguments.Count;
        call = argCount switch
        {
            3 => new GpuMacroCall(GpuMacroKind.ForUnroll, hasContext: false, lambdaArgumentIndex: 2),
            4 => new GpuMacroCall(GpuMacroKind.ForUnroll, hasContext: true, lambdaArgumentIndex: 3),
            _ => default
        };
        return call.Kind == GpuMacroKind.ForUnroll;
    }

    private bool TryBuildBoundExpression(
        ExpressionSyntax startExpr,
        ExpressionSyntax countExpr,
        [NotNullWhen(true)] out ExpressionSyntax? boundExpr)
    {
        boundExpr = null;
        if (semanticModel == null)
        {
            // <startExpr> + <countExpr>
            boundExpr = SyntaxFactory.BinaryExpression(
                SyntaxKind.AddExpression,
                startExpr,
                countExpr
            );
            return true;
        }

        if (!GpuCodeConstraints.TryGetMacroConstantInt(startExpr, semanticModel, out var startValue) ||
            !GpuCodeConstraints.TryGetMacroConstantInt(countExpr, semanticModel, out var countValue))
        {
            return false;
        }

        var sum = (long)startValue + countValue;
        if (sum is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        var bound = (int)sum;
        boundExpr = bound >= 0
            ? SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(bound)
            )
            : SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.UnaryMinusExpression,
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(-bound)
                )
            );
        return true;
    }

    // { <body statements> }
    private static BlockSyntax ToBodyBlock(SyntaxNode body, ImmutableArray<SyntaxAnnotation> annotations) =>
        body switch
        {
            BlockSyntax block => block,
            ExpressionSyntax expr => SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(expr).WithAdditionalAnnotations(annotations)
            ).WithAdditionalAnnotations(annotations),
            StatementSyntax stmt => SyntaxFactory.Block(stmt).WithAdditionalAnnotations(annotations),
            _ => throw new InvalidOperationException($"Cannot expand a ForUnroll body of kind '{body.Kind()}'.")
        };
}
