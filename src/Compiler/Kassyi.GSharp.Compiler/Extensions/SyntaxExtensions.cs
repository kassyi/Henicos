using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Compiler.Extensions;

internal static class SyntaxExtensions
{
    /// <summary>
    /// Checks if the token is of any of the specified kinds.
    /// </summary>
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

    /// <summary>
    /// Checks if the node is of any of the specified kinds.
    /// </summary>
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
}
