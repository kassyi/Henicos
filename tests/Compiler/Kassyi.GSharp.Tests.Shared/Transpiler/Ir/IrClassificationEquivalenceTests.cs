#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Classifies the IR of every GPU method of the generator snapshot inputs and the differential kernels (#1561), and
/// compares it with the syntax-based decisions the lowering and the emitters use today: the split point of each call
/// and the effects of each expression.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrClassificationEquivalenceTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_ClassifiesAsTheSyntaxPassesDo()
    {
        var stopwatch = Stopwatch.StartNew();
        var equivalence = new IrClassificationEquivalence();

        IrCoverageInputs.ForEach(equivalence.CompareAll);

        output.WriteLine($"{equivalence.CallsCompared} calls compared ({equivalence.SplitCallsCompared} split), " +
                         $"{equivalence.InvocationsInOpaque} invocations in opaque nodes " +
                         $"({equivalence.SplitInvocationsInOpaque} split), " +
                         $"{equivalence.ExpressionsCompared} expressions compared, " +
                         $"{equivalence.ExpressionsWithOpaque} skipped for an opaque node, " +
                         $"{stopwatch.ElapsedMilliseconds} ms");
        var unexplained = equivalence.EffectMismatches
            .Where(static pair =>
                !pair.Key.StartsWith(IrClassificationEquivalence.KnownPrefix, StringComparison.Ordinal))
            .SelectMany(static pair => pair.Value)
            .OrderBy(static m => m, StringComparer.Ordinal)
            .ToArray();
        var report = new List<string>();
        foreach (var (kind, mismatches) in equivalence.EffectMismatches
                     .OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{kind}: {mismatches.Count}");
            report.Add($"{kind}: {mismatches.Count}");
            report.AddRange(mismatches.OrderBy(static m => m, StringComparer.Ordinal).Select(static m => "  " + m));
        }

        if (unexplained.Length > 0)
        {
            // Every difference, the known ones included, to read when the test fails
            var receivedPath = RepositoryPaths.Combine("tmp", "IrClassificationEquivalence.received.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(receivedPath) ?? ".");
            File.WriteAllLines(receivedPath, report);
            output.WriteLine("Differences: " + receivedPath);
        }

        Assert.True(equivalence.SplitCallsCompared > 100, $"Only {equivalence.SplitCallsCompared} split calls.");
        Assert.True(equivalence.SplitMismatches.Count == 0,
            string.Join("\n", equivalence.SplitMismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(50)));
        Assert.True(unexplained.Length == 0, string.Join("\n", unexplained.Take(50)));
    }
}
