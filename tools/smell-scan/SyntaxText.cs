using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Renders syntax as text that does not depend on how the source is formatted.</summary>
internal static class SyntaxText
{
    /// <summary>
    /// Joins the tokens of <paramref name="node"/> without trivia, with one space only between two tokens that would
    /// otherwise run together (<c>ref int</c>, but <c>Dictionary&lt;string,int&gt;</c>).
    /// </summary>
    public static string Compact(SyntaxNode node)
    {
        var text = new System.Text.StringBuilder();
        foreach (var token in node.DescendantTokens())
        {
            var part = token.Text;
            if (part.Length == 0)
            {
                continue;
            }

            if (text.Length > 0 && IsWordCharacter(text[^1]) && IsWordCharacter(part[0]))
            {
                text.Append(' ');
            }

            text.Append(part);
        }

        return text.ToString();
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';
}
