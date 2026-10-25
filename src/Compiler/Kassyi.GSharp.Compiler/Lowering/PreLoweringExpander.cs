namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands macro statements (<c>Gpu.ForUnroll</c>) that contain phase barriers into standard <c>for</c> loops before
/// the state-machine lowering passes run.
/// </summary>
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
