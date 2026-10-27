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

    // { <statement 1> Gpu.SyncGrid(); <statement 2> ... }: a sync in each gap that SyncGridBody.IsSyncPoint places
    private BlockSyntax ExpandSyncGrid(ExpressionStatementSyntax node, LambdaExpressionSyntax body)
    {
        var annotations = ImmutableArray.CreateRange(
            node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
        );
        var elements = SyncGridBody.Flatten(body, semanticModel).Elements;
        var statements = new List<StatementSyntax>(elements.Length * 2);
        for (var i = 0; i < elements.Length; i++)
        {
            statements.Add(ToStatement(elements[i].Node, annotations));
            if (SyncGridBody.IsSyncPoint(elements, i, semanticModel))
            {
                statements.Add(CreateGridSync(annotations));
            }
        }

        return SyntaxFactory.Block(statements).WithAdditionalAnnotations(annotations);
    }

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

    // An expression body of a lambda becomes <expression>;
    private static StatementSyntax ToStatement(CSharpSyntaxNode node, ImmutableArray<SyntaxAnnotation> annotations) =>
        node switch
        {
            StatementSyntax statement => statement,
            ExpressionSyntax expression => SyntaxFactory.ExpressionStatement(expression).WithAdditionalAnnotations(annotations),
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
