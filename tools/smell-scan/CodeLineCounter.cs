using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Counts the lines of a node that hold at least one token. Blank lines, comment-only lines and preprocessor
/// directives are not counted; a token that spans lines (a verbatim or raw string) counts every line it covers.
/// Attributes and the signature are part of the node and are counted.
/// </summary>
internal static class CodeLineCounter
{
    /// <summary>Returns the number of code lines of <paramref name="node"/>.</summary>
    public static int Count(SourceText text, SyntaxNode node) => Count(text, node.Span, node.DescendantTokens());

    /// <summary>Returns the number of code lines of the nodes together, which lie inside <paramref name="span"/>.</summary>
    public static int Count(SourceText text, TextSpan span, IEnumerable<SyntaxToken> tokens)
    {
        var lines = text.Lines.GetLinePositionSpan(span);
        var firstLine = lines.Start.Line;
        var hasToken = new bool[lines.End.Line - firstLine + 1];
        var count = 0;
        foreach (var token in tokens)
        {
            if (token.Span.IsEmpty)
            {
                continue;
            }

            var tokenLines = text.Lines.GetLinePositionSpan(token.Span);
            for (var line = tokenLines.Start.Line; line <= tokenLines.End.Line; line++)
            {
                var index = line - firstLine;
                if (!hasToken[index])
                {
                    hasToken[index] = true;
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Returns the number of lines that <paramref name="node"/> spans.</summary>
    public static int PhysicalLines(SourceText text, SyntaxNode node) => PhysicalLines(text, node.Span);

    /// <summary>Returns the number of lines that <paramref name="span"/> covers.</summary>
    public static int PhysicalLines(SourceText text, TextSpan span)
    {
        var lines = text.Lines.GetLinePositionSpan(span);
        return lines.End.Line - lines.Start.Line + 1;
    }
}
