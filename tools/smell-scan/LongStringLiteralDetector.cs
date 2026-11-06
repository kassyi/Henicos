using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the lines that run past a column limit and hold a string literal token: a regular, verbatim, interpolated
/// (the text and the quotes), UTF-8 or single-line raw literal. Only tokens are looked at, so a long comment that
/// mentions a quote is not measured, and a long line without a string is not measured. The lines of a multi-line raw
/// string are measured under their own metric (<c>rawContentColumns</c>) and are judged only when the parameter
/// <c>include_multiline_raw_content</c> is not 0, because breaking such a line changes the value of the string.
/// A line is measured when it is longer than <see cref="MinColumns"/>, the smallest limit a rule may set.
/// The length of a line is counted in Unicode code points, with a tab as one.
/// </summary>
internal sealed class LongStringLiteralDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the longest accepted line.</summary>
    public const string MaxColumns = "max_columns";

    /// <summary>The parameter that is 0 to leave out the lines of multi-line raw strings and anything else to judge them.</summary>
    public const string IncludeRawContent = "include_multiline_raw_content";

    /// <summary>The metric of a line that holds a string literal of the usual kinds.</summary>
    public const string ColumnsMetric = "columns";

    /// <summary>The metric of a line that belongs to a multi-line raw string.</summary>
    public const string RawContentMetric = "rawContentColumns";

    /// <summary>The shortest line that is measured, and the smallest limit a rule may set.</summary>
    public const int MinColumns = 80;

    /// <inheritdoc />
    public override string Name => "LongStringLiteral";

    /// <inheritdoc />
    public override string Description => "A line past the column limit that contains a string literal.";

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxColumns, 120, MinColumns), new DetectorParameter(IncludeRawContent, 0, 0)];

    /// <inheritdoc />
    public override int ThresholdOf(Measurement measurement, RuleParameters parameters) => parameters.Get(MaxColumns);

    /// <inheritdoc />
    public override bool Exceeds(Measurement measurement, RuleParameters parameters)
    {
        if (measurement.Metric == RawContentMetric && parameters.Get(IncludeRawContent) == 0)
        {
            return false;
        }

        return measurement.Value > ThresholdOf(measurement, parameters);
    }

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        var lines = new Dictionary<int, LineInfo>();
        var order = new List<int>();
        foreach (var token in context.Root.DescendantTokens())
        {
            var isRaw = RawKind(token);
            if (isRaw is null)
            {
                continue;
            }

            var span = context.Text.Lines.GetLinePositionSpan(token.Span);
            for (var line = span.Start.Line; line <= span.End.Line; line++)
            {
                if (!lines.TryGetValue(line, out var info))
                {
                    info = new LineInfo(token.Parent);
                    lines.Add(line, info);
                    order.Add(line);
                }

                info.HasOther |= !isRaw.Value;
            }
        }

        foreach (var line in order)
        {
            AddIfLong(context, sink, line, lines[line]);
        }
    }

    private void AddIfLong(DetectorContext context, MeasurementSink sink, int line, LineInfo info)
    {
        var textLine = context.Text.Lines[line];
        var columns = CodePoints(context.Text, textLine.Span);
        if (columns <= MinColumns)
        {
            return;
        }

        var start = textLine.Start;
        while (start < textLine.End && char.IsWhiteSpace(context.Text[start]))
        {
            start++;
        }

        var range = SourceRange.From(context.Text, TextSpan.FromBounds(start, textLine.End));
        sink.Add(new Measurement(
            Name,
            info.HasOther ? ColumnsMetric : RawContentMetric,
            context.Path,
            range,
            info.Parent is null ? string.Empty : SyntaxSymbolNamer.Name(info.Parent),
            columns,
            1,
            textLine.Start));
    }

    private static int CodePoints(SourceText text, TextSpan span)
    {
        var count = 0;
        for (var i = span.Start; i < span.End; i++)
        {
            if (!char.IsLowSurrogate(text[i]))
            {
                count++;
            }
        }

        return count;
    }

    // Returns null for a token that is not part of a string literal, true for a multi-line raw string, false otherwise.
    private static bool? RawKind(SyntaxToken token)
    {
        switch (token.Kind())
        {
            case SyntaxKind.MultiLineRawStringLiteralToken:
            case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                return true;
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.Utf8StringLiteralToken:
            case SyntaxKind.SingleLineRawStringLiteralToken:
            case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
                return false;
            case SyntaxKind.InterpolatedStringStartToken:
            case SyntaxKind.InterpolatedVerbatimStringStartToken:
            case SyntaxKind.InterpolatedSingleLineRawStringStartToken:
            case SyntaxKind.InterpolatedMultiLineRawStringStartToken:
            case SyntaxKind.InterpolatedStringTextToken:
            case SyntaxKind.InterpolatedStringEndToken:
            case SyntaxKind.InterpolatedRawStringEndToken:
                return IsInMultiLineRawInterpolation(token);
            default:
                return null;
        }
    }

    private static bool IsInMultiLineRawInterpolation(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is InterpolatedStringExpressionSyntax expression)
            {
                return expression.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
            }
        }

        return false;
    }

    private sealed class LineInfo(SyntaxNode? parent)
    {
        public SyntaxNode? Parent { get; } = parent;

        public bool HasOther { get; set; }
    }
}
