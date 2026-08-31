namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides Roslyn syntax tree inspection and symbol resolution extension methods.</summary>
internal static class SyntaxExtensions
{
    /// <summary>
    /// Resolves the symbol for a member access expression, falling back to the enclosing invocation symbol if applicable.
    /// </summary>
    /// <param name="node">The member access expression syntax node.</param>
    /// <param name="semanticModel">The Roslyn semantic model.</param>
    /// <returns>The resolved symbol, or <c>null</c> if none found.</returns>
    public static ISymbol? ResolveMemberSymbol(this MemberAccessExpressionSyntax node, SemanticModel semanticModel) =>
        semanticModel.GetSymbolInfo(node).Symbol
        ?? (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node
            ? semanticModel.GetSymbolInfo(invocation).Symbol
            : null);

    /// <summary>
    /// Gets the symbol associated with the specified syntax node, cast to <typeparamref name="TSymbol"/>.
    /// </summary>
    /// <typeparam name="TSymbol">The expected symbol type.</typeparam>
    /// <param name="node">The syntax node.</param>
    /// <param name="semanticModel">The Roslyn semantic model.</param>
    /// <returns>The symbol cast to <typeparamref name="TSymbol"/>, or <c>null</c>.</returns>
    public static TSymbol? GetSymbol<TSymbol>(this SyntaxNode node, SemanticModel semanticModel) where TSymbol : class, ISymbol =>
        semanticModel.GetSymbolInfo(node).Symbol as TSymbol;

    /// <summary>
    /// Gets the type symbol associated with the specified syntax node.
    /// </summary>
    /// <param name="node">The syntax node.</param>
    /// <param name="semanticModel">The Roslyn semantic model.</param>
    /// <returns>The resolved <see cref="ITypeSymbol"/>, or <c>null</c>.</returns>
    public static ITypeSymbol? GetTypeSymbol(this SyntaxNode node, SemanticModel semanticModel) =>
        semanticModel.GetTypeInfo(node).Type;

    /// <summary>
    /// Unwraps a <see cref="RefExpressionSyntax"/> to its inner expression, or returns the original expression if not ref-wrapped.
    /// </summary>
    /// <param name="expression">The expression syntax to unwrap.</param>
    /// <returns>The unwrapped inner expression.</returns>
    public static ExpressionSyntax UnwrapRef(this ExpressionSyntax expression) =>
        expression is RefExpressionSyntax refExpr ? refExpr.Expression : expression;

    /// <summary>
    /// Determines whether the expression represents a default or empty value creation (e.g. <c>default</c>, <c>default(T)</c>, <c>new()</c>, <c>new T()</c>).
    /// </summary>
    /// <param name="expression">The expression syntax to inspect.</param>
    /// <returns><c>true</c> if the expression evaluates to a default zero-initialized value; otherwise, <c>false</c>.</returns>
    public static bool IsDefaultValueExpression(this ExpressionSyntax expression) =>
        expression is DefaultExpressionSyntax
            or LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression }
            or BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 }
            or ImplicitObjectCreationExpressionSyntax;

    /// <summary>
    /// Unwraps a <see cref="DeclarationExpressionSyntax"/> to its designation, or returns the original node if not a declaration expression.
    /// </summary>
    /// <param name="node">The syntax node to unwrap.</param>
    /// <returns>The inner variable designation, or the original syntax node.</returns>
    public static SyntaxNode UnwrapDesignation(this SyntaxNode node) =>
        node is DeclarationExpressionSyntax decl ? decl.Designation : node;

    /// <summary>
    /// Determines whether the argument has a <c>ref</c> or <c>out</c> modifier keyword.
    /// </summary>
    /// <param name="arg">The argument syntax node.</param>
    /// <returns><c>true</c> if the argument has ref or out keyword; otherwise, <c>false</c>.</returns>
    public static bool IsByRefOrOut(this ArgumentSyntax arg) =>
        arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

    /// <summary>
    /// Determines whether the parameter has a <c>ref</c> or <c>out</c> modifier keyword.
    /// </summary>
    /// <param name="param">The parameter syntax node.</param>
    /// <returns><c>true</c> if the parameter has ref or out modifier; otherwise, <c>false</c>.</returns>
    public static bool IsByRefOrOut(this ParameterSyntax param) =>
        param.Modifiers.Any(SyntaxKind.RefKeyword) || param.Modifiers.Any(SyntaxKind.OutKeyword);

    /// <summary>
    /// Determines whether the parameter has an <c>in</c> modifier keyword.
    /// </summary>
    /// <param name="param">The parameter syntax node.</param>
    /// <returns><c>true</c> if the parameter has in modifier; otherwise, <c>false</c>.</returns>
    public static bool IsIn(this ParameterSyntax param) =>
        param.Modifiers.Any(SyntaxKind.InKeyword);

    /// <summary>
    /// Determines whether the syntax node represents a discard expression (<c>_</c> or <c>discard</c>).
    /// </summary>
    /// <param name="node">The syntax node to inspect.</param>
    /// <param name="semanticModel">Optional semantic model to verify unbound identifier <c>_</c>.</param>
    /// <returns><c>true</c> if the node represents a discard; otherwise, <c>false</c>.</returns>
    public static bool IsDiscard(this SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is DiscardDesignationSyntax or OmittedTypeArgumentSyntax)
        {
            return true;
        }

        if (node is DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax })
        {
            return true;
        }

        if (node is IdentifierNameSyntax { Identifier.Text: "_" } identifier)
        {
            if (semanticModel == null)
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            return symbol is null or IDiscardSymbol;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the argument is an <c>out</c> discard parameter.
    /// </summary>
    /// <param name="arg">The argument syntax node.</param>
    /// <param name="semanticModel">Optional semantic model to verify unbound identifier <c>_</c>.</param>
    /// <returns><c>true</c> if the argument is an out discard; otherwise, <c>false</c>.</returns>
    public static bool IsOutDiscard(this ArgumentSyntax arg, SemanticModel? semanticModel = null) =>
        arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) &&
        (arg.Expression is IdentifierNameSyntax { Identifier.Text: "_" } ||
         arg.Expression is DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax } ||
         arg.Expression.IsDiscard(semanticModel));

    /// <summary>
    /// Resolves the corresponding <see cref="IParameterSymbol"/> for the given argument syntax in a method invocation.
    /// </summary>
    /// <param name="arg">The argument syntax node.</param>
    /// <param name="methodSymbol">The method symbol being invoked.</param>
    /// <returns>The matching parameter symbol, or <c>null</c> if not found.</returns>
    public static IParameterSymbol? DetermineParameter(this ArgumentSyntax arg, IMethodSymbol methodSymbol)
    {
        if (arg.NameColon != null)
        {
            var name = arg.NameColon.Name.Identifier.Text;
            foreach (var p in methodSymbol.Parameters)
            {
                if (p.Name == name)
                {
                    return p;
                }
            }

            return null;
        }

        if (arg.Parent is not ArgumentListSyntax argList)
        {
            return null;
        }

        var index = argList.Arguments.IndexOf(arg);
        if (index >= 0 && index < methodSymbol.Parameters.Length)
        {
            return methodSymbol.Parameters[index];
        }

        return null;
    }
}
