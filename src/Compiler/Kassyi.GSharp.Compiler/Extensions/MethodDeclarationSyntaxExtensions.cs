namespace Kassyi.GSharp.Compiler.Extensions;

internal static class MethodDeclarationSyntaxExtensions
{
    /// <summary>Whether <paramref name="method"/> has a block body or an expression body (<c>=&gt;</c>).</summary>
    public static bool HasBody(this MethodDeclarationSyntax method) =>
        method.Body != null || method.ExpressionBody != null;

    /// <summary>The block body or the expression body of <paramref name="method"/>, or <see langword="null"/> if it has neither.</summary>
    public static CSharpSyntaxNode? GetBodyNode(this MethodDeclarationSyntax method) =>
        (CSharpSyntaxNode?)method.Body ?? method.ExpressionBody;
}
