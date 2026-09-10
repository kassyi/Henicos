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
        foreach (var kind in kinds)
        {
            if (token.IsKind(kind))
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
        foreach (var kind in kinds)
        {
            if (node.IsKind(kind))
            {
                return true;
            }
        }
        return false;
    }
}
