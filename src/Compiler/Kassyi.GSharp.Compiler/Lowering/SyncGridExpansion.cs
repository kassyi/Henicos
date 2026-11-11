namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands a <c>Gpu.SyncGrid(body)</c> into the statements of its body with a <c>Gpu.SyncGrid()</c> in each gap
/// (<see cref="PreLoweringExpander"/>, <see cref="SyncGridBody"/>).
/// </summary>
internal static class SyncGridExpansion
{
    // { <statement 1> Gpu.SyncGrid(); <statement 2> ... }: a sync in each gap that SyncGridBody.IsSyncPoint places; a
    // nested body is a block of its own; a body that returns gets its done flag, labels and guards (SyncGridBody)
    public static BlockSyntax Expand(
        ExpressionStatementSyntax node,
        LambdaExpressionSyntax body,
        SemanticModel? semanticModel,
        ref int expansionCounter)
    {
        var annotations = MacroExpansionSyntax.GetOriginalSpans(node);
        var statements = SyncGridBody.Flatten(body, semanticModel);
        var bodyIds = new int[statements.BodyParents.Length];
        for (var i = 0; i < bodyIds.Length; i++)
        {
            bodyIds[i] = expansionCounter++;
        }

        var expansion = new Expansion(statements, bodyIds, semanticModel);
        var index = 0;
        return ExpandBody(expansion, bodyIndex: 0, ref index, annotations);
    }

    // The statements of body bodyIndex from statements.Elements[index] on, the bodies nested in it as blocks of their
    // own; returns with index at the first statement outside the body
    private static BlockSyntax ExpandBody(
        in Expansion expansion,
        int bodyIndex,
        ref int index,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var statements = expansion.Statements;
        var elements = statements.Elements;
        var head = ImmutableArray.CreateBuilder<StatementSyntax>();
        var rest = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (statements.BodyHasReturn(bodyIndex))
        {
            head.Add(CreateDoneFlag(DoneFlagName(expansion.BodyIds[bodyIndex])).WithAdditionalAnnotations(annotations));
        }

        while (index < elements.Length && statements.IsWithin(elements[index].BodyIndex, bodyIndex))
        {
            var element = elements[index];
            if (element.BodyIndex == bodyIndex)
            {
                ExpandStatement(expansion, index, head, rest);
                index++;
            }
            else
            {
                var child = statements.ChildOnPath(element.BodyIndex, bodyIndex);
                rest.Add(ExpandBody(expansion, child, ref index, annotations));
            }

            // The sync after a statement goes where the next statement is: after the blocks that end here, before the
            // ones that start with it
            if (index < elements.Length &&
                statements.IsWithin(elements[index].BodyIndex, bodyIndex) &&
                SyncGridBody.IsSyncPoint(elements, index - 1, expansion.SemanticModel))
            {
                rest.Add(CreateGridSync(annotations));
            }
        }

        head.AddRange(rest);
        return SyntaxFactory.Block(head).WithAdditionalAnnotations(annotations);
    }

    // One statement of a body: its returns rewritten, the label after it when it returns, the guard of the bodies that
    // returned before it
    private static void ExpandStatement(
        in Expansion expansion,
        int index,
        ImmutableArray<StatementSyntax>.Builder head,
        ImmutableArray<StatementSyntax>.Builder rest)
    {
        var element = expansion.Statements.Elements[index];
        var statement = ToStatement(element.Node);
        var bodyId = expansion.BodyIds[element.BodyIndex];
        var label = $"{GSharpInternalNames.Lowering.SyncGridNextLabelPrefix}{bodyId}_{index}";
        if (element.HasBodyReturn)
        {
            statement = SyncGridBody.RewriteReturns(statement, DoneFlagName(bodyId), label);
        }

        var guards = expansion.Statements.GetGuardBodies(index, expansion.SemanticModel);
        if (guards.IsEmpty)
        {
            rest.Add(statement);
        }
        else if (CreateGuarded(statement, guards, expansion, head) is { } guarded)
        {
            rest.Add(guarded);
        }

        if (element.HasBodyReturn)
        {
            // <label>: ;
            // No original-span annotation, as for the labels of LoopRewriter and BranchRewriter: the label is keyed by
            // its name, not by the symbol of the statement it follows (a foreach variable, a label of the user)
            rest.Add(LoweringSyntax.EmptyLabel(label));
        }
    }

    // if (!<flag 1> && ...) { <statement> } with the span of statement, or null when nothing is left to guard; a
    // guarded declaration and the out variables of a guarded statement are declared in head and assigned in the guard
    private static IfStatementSyntax? CreateGuarded(
        StatementSyntax statement,
        ImmutableArray<int> guards,
        in Expansion expansion,
        ImmutableArray<StatementSyntax>.Builder head)
    {
        var elementAnnotations = statement.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        var guarded = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (statement is LocalDeclarationStatementSyntax declaration &&
            SyncGridBody.CanSplit(declaration, expansion.SemanticModel))
        {
            SplitDeclaration(declaration, expansion.SemanticModel, head, guarded);
        }
        else
        {
            guarded.Add(statement);
        }

        for (var i = 0; i < guarded.Count; i++)
        {
            guarded[i] = MoveOutVariables(guarded[i], expansion.SemanticModel, head);
        }

        return guarded.Count > 0
            ? CreateGuard(guards, expansion.BodyIds, guarded).WithAdditionalAnnotations(elementAnnotations)
            : null;
    }

    // <type> <name>; for each declarator into head; <name> = <initializer>; for each initializer into assignments
    private static void SplitDeclaration(
        LocalDeclarationStatementSyntax declaration,
        SemanticModel? semanticModel,
        ImmutableArray<StatementSyntax>.Builder head,
        ImmutableArray<StatementSyntax>.Builder assignments)
    {
        var annotations = declaration.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
        foreach (var declarator in declaration.Declaration.Variables)
        {
            // <type> <name>;
            var moved = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    GetDeclaredType(declaration.Declaration.Type, declarator, semanticModel),
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
                .WithOriginalSpanFrom(declarator);
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
    private static StatementSyntax MoveOutVariables(
        StatementSyntax statement,
        SemanticModel? semanticModel,
        ImmutableArray<StatementSyntax>.Builder head)
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
                .WithOriginalSpanFrom(designation);
            // <type> <name>;
            head.Add(
                SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(type, SyntaxFactory.SingletonSeparatedList(declarator))
                ).WithOriginalSpanFrom(variable)
            );
        }

        return SyncGridBody.RewriteOutVariables(statement, variables);
    }

    // var becomes the type of the local; without a model it stays var
    private static TypeSyntax GetDeclaredType(
        TypeSyntax declared,
        VariableDeclaratorSyntax declarator,
        SemanticModel? semanticModel) =>
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
    // The statement carries the span of the Gpu.SyncGrid(body) it came from, for diagnostics. That span does not lead
    // back to a symbol, so the call gets its grid barrier annotation here, as SpanAnnotationRewriter gives one to a
    // written Gpu.SyncGrid() (BarrierHelper and the state machine read only the annotation).
    private static ExpressionStatementSyntax CreateGridSync(ImmutableArray<SyntaxAnnotation> annotations) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(TypeConstants.GpuType),
                    SyntaxFactory.IdentifierName(TypeConstants.GpuMacros.SyncGrid)
                )
            ).WithAdditionalAnnotations(
                new SyntaxAnnotation(
                    LoweringAnnotations.SyncIntrinsicAnnotationKind,
                    LoweringAnnotations.GridBarrierAnnotationData
                )
            )
        ).WithAdditionalAnnotations(annotations);

    // An expression body of a lambda becomes <expression>; with the span of the expression
    private static StatementSyntax ToStatement(CSharpSyntaxNode node) =>
        node switch
        {
            StatementSyntax statement => statement,
            ExpressionSyntax expression =>
                SyntaxFactory.ExpressionStatement(expression).WithOriginalSpanFrom(expression),
            _ => throw new InvalidOperationException($"Cannot expand a SyncGrid body element of kind '{node.Kind()}'.")
        };

    // The flattened body, the expansion number of each of its bodies, and the model that SyncGridBody decides with
    private readonly record struct Expansion(
        SyncGridStatements Statements,
        int[] BodyIds,
        SemanticModel? SemanticModel);
}
