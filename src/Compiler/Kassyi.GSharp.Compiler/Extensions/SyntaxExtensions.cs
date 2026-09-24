namespace Kassyi.GSharp.Compiler.Extensions;

internal static class SyntaxExtensions
{
#pragma warning disable RS1034 // Prefer 'IsKind' for checking syntax kinds (We are implementing IsKind itself)

    // --------------------------------------------------------
    // SyntaxToken IsKind Overloads (Zero Allocation)
    // --------------------------------------------------------

    public static bool IsKind(this SyntaxToken token, SyntaxKind kind1, SyntaxKind kind2)
    {
        var tokenKind = token.Kind();
        return tokenKind == kind1 || tokenKind == kind2;
    }

    public static bool IsKind(this SyntaxToken token, SyntaxKind kind1, SyntaxKind kind2, SyntaxKind kind3)
    {
        var tokenKind = token.Kind();
        return tokenKind == kind1 || tokenKind == kind2 || tokenKind == kind3;
    }

    public static bool IsKind(this SyntaxToken token, SyntaxKind kind1, SyntaxKind kind2, SyntaxKind kind3, SyntaxKind kind4)
    {
        var tokenKind = token.Kind();
        return tokenKind == kind1 || tokenKind == kind2 || tokenKind == kind3 || tokenKind == kind4;
    }

    // Fallback for > 4 elements
    public static bool IsKind(this SyntaxToken token, params SyntaxKind[] kinds)
    {
        var tokenKind = token.Kind();
        foreach (var kind in kinds)
        {
            if (tokenKind == kind)
            {
                return true;
            }
        }
        return false;
    }

    // --------------------------------------------------------
    // SyntaxNode IsKind Overloads (Zero Allocation)
    // --------------------------------------------------------

    public static bool IsKind(this SyntaxNode node, SyntaxKind kind1, SyntaxKind kind2)
    {
        var nodeKind = node.Kind();
        return nodeKind == kind1 || nodeKind == kind2;
    }

    public static bool IsKind(this SyntaxNode node, SyntaxKind kind1, SyntaxKind kind2, SyntaxKind kind3)
    {
        var nodeKind = node.Kind();
        return nodeKind == kind1 || nodeKind == kind2 || nodeKind == kind3;
    }

    public static bool IsKind(this SyntaxNode node, SyntaxKind kind1, SyntaxKind kind2, SyntaxKind kind3, SyntaxKind kind4)
    {
        var nodeKind = node.Kind();
        return nodeKind == kind1 || nodeKind == kind2 || nodeKind == kind3 || nodeKind == kind4;
    }

    // Fallback for > 4 elements
    public static bool IsKind(this SyntaxNode node, params SyntaxKind[] kinds)
    {
        var nodeKind = node.Kind();
        foreach (var kind in kinds)
        {
            if (nodeKind == kind)
            {
                return true;
            }
        }
        return false;
    }

#pragma warning restore RS1034
}
