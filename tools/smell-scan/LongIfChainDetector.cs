using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the branches of an <c>if</c> / <c>else if</c> chain: the first <c>if</c>, every <c>else if</c> and a final
/// <c>else</c> each count as one. An <c>if</c> inside an <c>else { }</c> block starts a chain of its own. Chains of fewer
/// than three branches are not measured.
/// </summary>
internal sealed class LongIfChainDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted number of branches.</summary>
    public const string MaxBranches = "max_branches";

    /// <summary>The smallest chain that is measured.</summary>
    private const int MinMeasuredBranches = 3;

    /// <inheritdoc />
    public override string Name => "LongIfChain";

    /// <inheritdoc />
    public override string Description => "A chain of if / else if branches that is long.";

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxBranches, 6, MinMeasuredBranches)];

    /// <inheritdoc />
    public override int ThresholdOf(Measurement measurement, RuleParameters parameters) => parameters.Get(MaxBranches);

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        foreach (var node in context.Root.DescendantNodes())
        {
            if (node is not IfStatementSyntax { Parent: not ElseClauseSyntax } first)
            {
                continue;
            }

            var branches = CountBranches(first);
            if (branches < MinMeasuredBranches)
            {
                continue;
            }

            sink.Add(new Measurement(
                Name,
                "branches",
                context.Path,
                SourceRange.From(context.Text, first),
                SyntaxSymbolNamer.Name(first),
                branches,
                CodeLineCounter.PhysicalLines(context.Text, first),
                first.IfKeyword.SpanStart,
                NodeKey: SyntaxText.Hash(first)));
        }
    }

    private static int CountBranches(IfStatementSyntax first)
    {
        var branches = 1;
        var current = first;
        while (current.Else is { } elseClause)
        {
            branches++;
            if (elseClause.Statement is not IfStatementSyntax next)
            {
                break;
            }

            current = next;
        }

        return branches;
    }
}
