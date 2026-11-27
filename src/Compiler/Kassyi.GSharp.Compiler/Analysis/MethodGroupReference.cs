namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A method named without being called (<c>U.F</c>, <c>F</c>), outside <c>nameof</c>: a method group that device
/// code would name.
/// </summary>
internal static class MethodGroupReference
{
    /// <summary>
    /// Whether <paramref name="node"/> names a method without calling it, outside the <c>nameof</c> operator
    /// (<see cref="NameofOperator"/>). The name of a conditional call (<c>u?.F()</c>) is called.
    /// </summary>
    public static bool Is(SyntaxNode node, SemanticModel semanticModel)
    {
        var name = node.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == node => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => node
        };
        while (name.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            name = parenthesized;
        }

        if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name)
        {
            return false;
        }

        return !NameofOperator.Encloses(name, semanticModel);
    }
}
