using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Renders syntax as text that does not depend on how the source is formatted.</summary>
internal static class SyntaxText
{
    /// <summary>
    /// Joins the tokens of <paramref name="node"/> without trivia, with one space only between two tokens that would
    /// otherwise run together (<c>ref int</c>, <c>List&lt;int&gt; Items</c>, but <c>Dictionary&lt;string,int&gt;</c>).
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

            if (text.Length > 0 && NeedsSpace(text[^1], part[0]))
            {
                text.Append(' ');
            }

            text.Append(part);
        }

        return text.ToString();
    }

    // A name that follows a closing type token is an element name of a tuple: List<int> Items, int[] Numbers, string? Name.
    private static bool NeedsSpace(char previous, char next) =>
        IsWordCharacter(next) && (IsWordCharacter(previous) || previous is '>' or ']' or '?');

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';
}
