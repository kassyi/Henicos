namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Rewrites the body of the lambda of <c>Gpu.ForUnroll</c> / <c>Gpu.GridStrideLoop</c> when the macro becomes a loop:
/// the loop parameter becomes the loop variable, and the context parameter of the <c>ref ctx</c> overloads becomes the
/// context argument. A parameter of a nested lambda or local function that has the same name hides the macro parameter.
/// </summary>
internal sealed class MacroLambdaSubstituter(
    SemanticModel? semanticModel,
    ParameterSyntax loopParam,
    string unrollVarName,
    ParameterSyntax? contextParam,
    ExpressionSyntax? contextArgExpr) : CSharpSyntaxRewriter
{
    private readonly string _loopParamName = loopParam.Identifier.ValueText;
    private readonly IParameterSymbol? _loopParamSymbol =
        semanticModel?.GetDeclaredSymbol<IParameterSymbol>(loopParam)?.OriginalDefinition;
    private readonly string? _contextParamName = contextParam?.Identifier.ValueText;
    private readonly IParameterSymbol? _contextParamSymbol =
        contextParam != null
            ? semanticModel?.GetDeclaredSymbol<IParameterSymbol>(contextParam)?.OriginalDefinition
            : null;
    private int _loopParamShadowDepth;
    private int _contextParamShadowDepth;

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (IsNonValueIdentifierPosition(node))
        {
            return base.VisitIdentifierName(node);
        }

        if (MatchesParameter(node, _loopParamName, _loopParamSymbol, _loopParamShadowDepth))
        {
            // Intentionally omit OriginalSpanAnnotationKind so the reference binds to the synthesized loop variable
            return SyntaxFactory.IdentifierName(unrollVarName).WithTriviaFrom(node);
        }

        if (contextArgExpr != null &&
            _contextParamName != null &&
            MatchesParameter(node, _contextParamName, _contextParamSymbol, _contextParamShadowDepth))
        {
            return contextArgExpr.WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        var paramName = node.Parameter.Identifier.ValueText;
        PushShadow(paramName);
        try
        {
            return base.VisitSimpleLambdaExpression(node);
        }
        finally
        {
            PopShadow(paramName);
        }
    }

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        VisitWithParameterList(node, node.ParameterList, base.VisitParenthesizedLambdaExpression);

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        node.ParameterList != null
            ? VisitWithParameterList(node, node.ParameterList, base.VisitAnonymousMethodExpression)
            : base.VisitAnonymousMethodExpression(node);

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        VisitWithParameterList(node, node.ParameterList, base.VisitLocalFunctionStatement);

    private SyntaxNode? VisitWithParameterList<TNode>(
        TNode node,
        ParameterListSyntax parameterList,
        Func<TNode, SyntaxNode?> visitBase) where TNode : SyntaxNode
    {
        foreach (var parameter in parameterList.Parameters)
        {
            PushShadow(parameter.Identifier.ValueText);
        }

        try
        {
            return visitBase(node);
        }
        finally
        {
            foreach (var parameter in parameterList.Parameters)
            {
                PopShadow(parameter.Identifier.ValueText);
            }
        }
    }

    private void PushShadow(string name)
    {
        if (string.Equals(name, _loopParamName, StringComparison.Ordinal))
        {
            _loopParamShadowDepth++;
        }

        if (_contextParamName != null && string.Equals(name, _contextParamName, StringComparison.Ordinal))
        {
            _contextParamShadowDepth++;
        }
    }

    private void PopShadow(string name)
    {
        if (string.Equals(name, _loopParamName, StringComparison.Ordinal))
        {
            _loopParamShadowDepth--;
        }

        if (_contextParamName != null && string.Equals(name, _contextParamName, StringComparison.Ordinal))
        {
            _contextParamShadowDepth--;
        }
    }

    private bool MatchesParameter(
        IdentifierNameSyntax node,
        string paramName,
        IParameterSymbol? paramSymbol,
        int shadowDepth)
    {
        if (shadowDepth > 0)
        {
            return false;
        }

        if (semanticModel != null && paramSymbol != null)
        {
            return semanticModel.GetSymbol(node)?.OriginalDefinition is { } symbol &&
                   symbol.IsEqualTo(paramSymbol);
        }

        return string.Equals(node.Identifier.ValueText, paramName, StringComparison.Ordinal);
    }

    private static bool IsNonValueIdentifierPosition(IdentifierNameSyntax node) =>
        node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == node,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name == node,
            QualifiedNameSyntax => true,
            NameColonSyntax => true,
            NameEqualsSyntax => true,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment => assignment.Left == node,
            GotoStatementSyntax => true,
            _ => false
        };
}
