#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Plans the split of every GPU method and emitted struct member of the generator snapshot inputs and the
/// differential kernels on the IR (#1561) and compares the plan with what the syntax-based lowering passes evaluate
/// before each split point today.
/// </summary>
/// <remarks>
/// The plan reduced to the rules the syntax passes implement must match them exactly. The full plan differs where
/// the syntax passes evaluate an operand after a split that C# evaluates before it (#991); the number of such
/// statements per reason, and of the statements the plan leaves to the syntax passes, is kept in
/// <c>IrSpillDifferences.expected.txt</c>. Every run writes the full list to the received file it names.
/// </remarks>
[Category(TestCategory.Compiler)]
public class IrSpillEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrSpillDifferences.expected.txt";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_PlansWhatTheSyntaxPassesSpillPlusTheMissingRules()
    {
        var stopwatch = Stopwatch.StartNew();
        var equivalence = new IrSpillEquivalence();

        IrCoverageInputs.ForEach(equivalence.CompareAll);

        output.WriteLine($"{equivalence.StatementsCompared} planned statements compared, " +
                         $"{equivalence.Unexplained.Count} unexplained, {stopwatch.ElapsedMilliseconds} ms");
        WriteDifferences(equivalence);

        Assert.True(equivalence.StatementsCompared > 100,
            $"Only {equivalence.StatementsCompared} planned statements.");
        Assert.True(equivalence.Unexplained.Count == 0,
            string.Join("\n", equivalence.Unexplained.OrderBy(static m => m, StringComparer.Ordinal).Take(30)));
        AssertCountsMatch(equivalence);
    }

    private void WriteDifferences(IrSpillEquivalence equivalence)
    {
        var report = new List<string>();
        foreach (var (reason, differences) in equivalence.Differences
                     .OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{reason}: {differences.Count}");
            report.Add($"{reason}: {differences.Count}");
            report.AddRange(differences.OrderBy(static m => m, StringComparer.Ordinal).Select(static m => "  " + m));
        }

        report.Add($"unexplained: {equivalence.Unexplained.Count}");
        report.AddRange(equivalence.Unexplained
            .OrderBy(static m => m, StringComparer.Ordinal)
            .Select(static m => "  " + m));
        var receivedPath = RepositoryPaths.Combine("tmp", "IrSpillEquivalence.received.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(receivedPath) ?? ".");
        File.WriteAllLines(receivedPath, report);
        output.WriteLine("Differences: " + receivedPath);
    }

    private void AssertCountsMatch(IrSpillEquivalence equivalence)
    {
        var actual = string.Join("\n", equivalence.Differences
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key + "\t" + pair.Value.Count)) + "\n";
        var expectedPath = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Transpiler",
            "Ir", ExpectedFile);
        var expected = File.Exists(expectedPath) ? string.Join("\n", File.ReadAllLines(expectedPath)) + "\n" : "";
        if (expected != actual)
        {
            var receivedPath = RepositoryPaths.Combine("tmp", "IrSpillDifferences.received.txt");
            File.WriteAllText(receivedPath, actual);
            output.WriteLine("Received counts: " + receivedPath);
        }

        Assert.Equal(expected, actual);
    }
}
