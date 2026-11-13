#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Plans the split of every GPU method and emitted struct member of the generator snapshot inputs and of every kernel
/// file of the shared tests on the IR (#1561) and compares the plan with what the syntax-based lowering passes evaluate
/// before each split point today.
/// </summary>
/// <remarks>
/// The plan reduced to the rules the syntax passes implement must match them exactly. Every statement where the full
/// plan differs (the syntax passes evaluate an operand after a split that C# evaluates before it) is kept, with its
/// reason and both traces, in <c>IrSpillDifferences.expected.txt</c>, compared line by line. The statements the plan
/// leaves to the syntax passes are counted in the output only. Every run writes what it found to the received file it
/// names, one per test assembly.
/// </remarks>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrSpillEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrSpillDifferences.expected.txt";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_PlansWhatTheSyntaxPassesSpillPlusTheMissingRules()
    {
        var stopwatch = Stopwatch.StartNew();
        var equivalence = new IrSpillEquivalence();

        IrCoverageInputs.ForEach(equivalence.CompareAll, everyKernelFile: true);

        output.WriteLine($"{equivalence.StatementsCompared} planned statements compared, " +
                         $"{equivalence.Unexplained.Count} unexplained, {stopwatch.ElapsedMilliseconds} ms");
        foreach (var (reason, count) in equivalence.Fallbacks.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"left to the syntax passes, {reason}: {count}");
        }

        output.WriteLine($"{equivalence.StatementsRewritten} planned statements rewritten, " +
                         $"{equivalence.StatementsWithACheck} with a check before the split");
        foreach (var (reason, count) in equivalence.Skips.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"rewrite skipped, {reason}: {count}");
        }

        output.WriteLine($"{equivalence.MixedMethods.Count} methods mix rewritten statements and statements left to " +
                         "the syntax passes:");
        foreach (var method in equivalence.MixedMethods.OrderBy(static m => m, StringComparer.Ordinal))
        {
            output.WriteLine("  " + method);
        }

        var actual = IrExpectedFiles.SortedLines(equivalence.Known);
        WriteReceived(equivalence, actual);

        Assert.True(equivalence.StatementsCompared > 100,
            $"Only {equivalence.StatementsCompared} planned statements.");
        Assert.True(equivalence.Unexplained.Count == 0,
            string.Join("\n", equivalence.Unexplained.OrderBy(static m => m, StringComparer.Ordinal).Take(30)));
        Assert.True(equivalence.Violations.Count == 0,
            string.Join("\n", equivalence.Violations.OrderBy(static m => m, StringComparer.Ordinal).Take(30)));
        IrExpectedFiles.AssertMatches(ExpectedFile, actual, output);
    }

    // On every run, with the unexplained statements after the known ones
    private void WriteReceived(IrSpillEquivalence equivalence, string known)
    {
        var receivedPath = IrExpectedFiles.ReceivedPath("IrSpillDifferences");
        var unexplained = equivalence.Unexplained
            .OrderBy(static m => m, StringComparer.Ordinal)
            .Select(static m => "UNEXPLAINED " + m);
        File.WriteAllText(receivedPath, known + string.Join("\n", unexplained));
        output.WriteLine("Received: " + receivedPath);
    }
}
