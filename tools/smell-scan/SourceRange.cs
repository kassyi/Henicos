using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A source range with 1-based lines and columns; the end column is the column after the last character.</summary>
internal readonly record struct SourceRange(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>Gets the range of <paramref name="span"/> in <paramref name="text"/>.</summary>
    public static SourceRange From(SourceText text, TextSpan span)
    {
        var lines = text.Lines.GetLinePositionSpan(span);
        return new SourceRange(
            lines.Start.Line + 1,
            lines.Start.Character + 1,
            lines.End.Line + 1,
            lines.End.Character + 1);
    }

    /// <summary>Gets the range of <paramref name="node"/> without its leading and trailing trivia.</summary>
    public static SourceRange From(SourceText text, SyntaxNode node) => From(text, node.Span);
}
