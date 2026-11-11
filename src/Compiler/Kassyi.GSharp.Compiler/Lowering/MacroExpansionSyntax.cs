namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The syntax pieces that the expansions of <see cref="PreLoweringExpander"/> share: the parts of a macro loop lambda,
/// its substituted body, and the declarations and prelude calls the expanded loops are made of.
/// </summary>
internal static class MacroExpansionSyntax
{
    /// <summary>The original-span annotations of <paramref name="node"/>, which its expansion carries.</summary>
    public static ImmutableArray<SyntaxAnnotation> GetOriginalSpans(SyntaxNode node) =>
        ImmutableArray.CreateRange(node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));

    /// <summary>
    /// The body lambda of a <c>Gpu.ForUnroll</c> or <c>Gpu.GridStrideLoop</c> call with its loop parameter and context,
    /// when the body holds a phase barrier and names its loop parameter.
    /// </summary>
    public static bool TryGetLoopLambda(
        GpuMacroCall call,
        SeparatedSyntaxList<ArgumentSyntax> args,
        out MacroLoopLambda loop)
    {
        loop = default;
        if (call.GetBody(args) is not { } lambdaExpr ||
            !BarrierHelper.ContainsPhaseBarrier(lambdaExpr.Body) ||
            GpuCodeConstraints.GetMacroLoopParameter(lambdaExpr, call.HasContext) is not { } loopParam ||
            loopParam.Identifier.ValueText.Length == 0)
        {
            return false;
        }

        ParameterSyntax? contextParam = null;
        ExpressionSyntax? contextArgExpr = null;
        if (call.GetContext(args) is { } contextArgument &&
            lambdaExpr is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenLambda)
        {
            contextParam = parenLambda.ParameterList.Parameters[0];
            contextArgExpr = contextArgument.WithoutTrivia();
        }

        loop = new MacroLoopLambda(lambdaExpr, loopParam, contextParam, contextArgExpr);
        return true;
    }

    // { <body statements> }: the lambda body with its loop parameter replaced by indexName and its context parameter by
    // the context argument (MacroLambdaSubstituter)
    public static BlockSyntax SubstituteBody(
        in MacroLoopLambda loop,
        SemanticModel? semanticModel,
        string indexName,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var substituter = new MacroLambdaSubstituter(
            semanticModel,
            loop.LoopParameter,
            indexName,
            loop.ContextParameter,
            loop.ContextArgument
        );
        var body = loop.Lambda.Body;
        return ToBodyBlock(substituter.Visit(body) ?? body, annotations);
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

    // <name>(<argument>?): a prelude helper, which the managed path reaches through using static ManagedGpuContext
    public static InvocationExpressionSyntax CallPrelude(string name, ExpressionSyntax? argument = null) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(name),
            argument == null
                ? SyntaxFactory.ArgumentList()
                : SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argument)))
        );

    // <type> <name> = <value>
    public static VariableDeclarationSyntax DeclareVariable(
        SyntaxKind type,
        string name,
        ExpressionSyntax value,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var declarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
            .WithInitializer(SyntaxFactory.EqualsValueClause(value));
        return SyntaxFactory.VariableDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(type)),
            SyntaxFactory.SingletonSeparatedList(declarator)
        ).WithAdditionalAnnotations(annotations);
    }

    // <type> <name> = <value>;
    public static LocalDeclarationStatementSyntax DeclareLocal(
        SyntaxKind type,
        string name,
        ExpressionSyntax value,
        ImmutableArray<SyntaxAnnotation> annotations) =>
        SyntaxFactory.LocalDeclarationStatement(DeclareVariable(type, name, value, annotations))
            .WithAdditionalAnnotations(annotations);
}
