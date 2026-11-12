#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Classifies the IR of every GPU method and emitted struct member of the generator snapshot inputs and the
/// differential kernels (#1561), and compares it with the syntax-based decisions the lowering and the emitters use
/// today: the split point of each call and the effects of each expression.
/// </summary>
/// <remarks>
/// The number of differences per known reason is kept in <c>IrEffectDifferences.expected.txt</c>: a reason that grows
/// or a new one fails, so a change that falls into a known reason by accident is seen. Every run writes the full list
/// of differences to the received file it names in its output.
/// </remarks>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrClassificationEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrEffectDifferences.expected.txt";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_ClassifiesAsTheSyntaxPassesDo()
    {
        var stopwatch = Stopwatch.StartNew();
        var equivalence = new IrClassificationEquivalence();

        IrCoverageInputs.ForEach(equivalence.CompareAll);

        output.WriteLine($"{equivalence.CallsCompared} calls compared ({equivalence.SplitCallsCompared} split), " +
                         $"{equivalence.InvocationsInOpaque} invocations in opaque nodes " +
                         $"({equivalence.SplitInvocationsInOpaque} split, all under nodes marked as holding one), " +
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
        IrExpectedFiles.AssertMatches(ExpectedFile,
            IrExpectedFiles.SortedLines(equivalence.EffectMismatches
                .Select(static pair => pair.Key + "\t" + pair.Value.Count)
            ), output);
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
