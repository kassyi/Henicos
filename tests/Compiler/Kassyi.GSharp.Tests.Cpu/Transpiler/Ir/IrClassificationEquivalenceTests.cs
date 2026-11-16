#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Classifies the IR of every GPU method and emitted struct member of the generator snapshot inputs and the
/// differential kernels (#1561), and compares it with the syntax-based decisions the lowering and the emitters use
/// today: the split point of each call and the effects of each expression.
/// </summary>
/// <remarks>
/// The number of differences per method and known reason is kept in <c>IrEffectDifferences.expected.yml</c>: a reason
/// that grows or a new one fails, so a change that falls into a known reason by accident is seen. Every run writes the
/// full list of differences to the received file it names in its output.
/// </remarks>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrClassificationEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrEffectDifferences.expected.yml";

    private static readonly string[] s_header =
    [
        "The expressions whose effects the IR classifies differently from the syntax walk of the lowering and the",
        "emitters (ExpressionEffects), per GPU method or emitted struct member of the IR corpus, for a known reason.",
        "IrClassificationEquivalenceTests.EveryGpuMethod_ClassifiesAsTheSyntaxPassesDo builds the list again on every",
        "run and fails on any difference; a received file goes to tmp/, and the full list of differences to",
        "tmp/IrClassificationEquivalence.<assembly>.received.txt. The test output prints the total of each reason.",
        "",
        "  file:   the source file, relative to tests/Compiler/Kassyi.GSharp.Tests.Shared",
        "  method: the containing type and the method (an accessor, constructor or operator by its metadata name)",
        "  reason: the known reason of the difference (IrEffectDifferences.Reason)",
        "  count:  how many such expressions the method has"
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_ClassifiesAsTheSyntaxPassesDo()
    {
        var stopwatch = Stopwatch.StartNew();
        var equivalence = new IrClassificationEquivalence();

        IrCoverageInputs.ForEach(equivalence.CompareAll);

        output.WriteLine($"{equivalence.CallsCompared} calls compared ({equivalence.SplitCallsCompared} split), " +
                         $"{equivalence.InvocationsInOpaque} invocations in opaque nodes " +
                         $"({equivalence.SplitInvocationsInOpaque} split, all under nodes marked as holding one, " +
                         $"{equivalence.RemovedSplitInvocationsInOpaque} split in calls C# removes), " +
                         $"{equivalence.StructMembers} struct members, " +
                         $"{equivalence.ExpressionsCompared} expressions compared, " +
                         $"{equivalence.ExpressionsWithOpaque} skipped for an opaque node, " +
                         $"{equivalence.OuterExplainedByOperands} outer differences explained by their operands, " +
                         $"{stopwatch.ElapsedMilliseconds} ms");
        WriteDifferences(equivalence);

        Assert.True(equivalence.SplitCallsCompared > 100, $"Only {equivalence.SplitCallsCompared} split calls.");
        Assert.True(equivalence.SplitInvocationsInOpaque > 50,
            $"Only {equivalence.SplitInvocationsInOpaque} split points in opaque nodes.");
        Assert.True(equivalence.StructMembers > 50, $"Only {equivalence.StructMembers} struct members.");
        Assert.True(equivalence.SplitMismatches.Count == 0,
            string.Join("\n", equivalence.SplitMismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(50)));
        var unexplained = equivalence.EffectMismatches
            .Where(static pair =>
                !pair.Key.StartsWith(IrClassificationEquivalence.KnownPrefix, StringComparison.Ordinal))
            .SelectMany(static pair => pair.Value)
            .OrderBy(static m => m, StringComparer.Ordinal)
            .ToArray();
        Assert.True(unexplained.Length == 0, string.Join("\n", unexplained.Take(50)));
        foreach (var total in equivalence.EffectCounts.Totals())
        {
            output.WriteLine(total);
        }

        IrExpectedFiles.AssertMatches(ExpectedFile, equivalence.EffectCounts.Render(s_header), output);
    }

    // Every difference, to read when a count changes
    private void WriteDifferences(IrClassificationEquivalence equivalence)
    {
        var report = new List<string>();
        foreach (var (reason, differences) in equivalence.EffectMismatches
                     .OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{reason}: {differences.Count}");
            report.Add($"{reason}: {differences.Count}");
            report.AddRange(differences.OrderBy(static m => m, StringComparer.Ordinal).Select(static m => "  " + m));
        }

        var receivedPath = IrExpectedFiles.ReceivedPath("IrClassificationEquivalence");
        File.WriteAllLines(receivedPath, report);
        output.WriteLine("Differences: " + receivedPath);
    }
}
