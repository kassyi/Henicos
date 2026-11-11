#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Builds the IR of every GPU method of the generator snapshot inputs (<c>TestCases/</c>) and of the differential
/// kernels (#1561): the builder never throws, every tree keeps the invariants, and what stays opaque, counted by
/// reason, operation kind and syntax kind, matches <c>IrOpaqueCoverage.expected.txt</c>. A change that makes more
/// constructs opaque fails; one that builds more of them updates the table, which shows how far the IR reaches.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrCoverageTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrOpaqueCoverage.expected.txt";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_BuildsAndKeepsTheInvariants()
    {
        var stopwatch = Stopwatch.StartNew();
        var opaque = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var violations = new ConcurrentBag<string>();
        var built = 0;

        var (fileCount, differentialCount) = IrCoverageInputs.ForEach((compilation, trees) =>
            Interlocked.Add(ref built, IrCoverage.BuildAll(compilation, trees, opaque, violations)));

        output.WriteLine($"{fileCount} test case files, {differentialCount} differential files, " +
                         $"{built} methods in {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(built > 500, $"Only {built} GPU methods were built.");
        Assert.True(violations.IsEmpty,
            string.Join("\n", violations.OrderBy(static v => v, StringComparer.Ordinal).Take(100)));
        AssertTableMatches(opaque);
    }

    private void AssertTableMatches(ConcurrentDictionary<string, int> opaque)
    {
        var actual = string.Join("\n", opaque
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key + "\t" + pair.Value)) + "\n";
        var expectedPath = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Transpiler",
            "Ir", ExpectedFile);
        var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath).Replace("\r", "") : "";
        if (expected != actual)
        {
            // The table of this run, to compare with the expected one and copy over it when the change is intended
            var receivedPath = RepositoryPaths.Combine("tmp",
                $"IrOpaqueCoverage.{typeof(IrCoverageTests).Assembly.GetName().Name}.received.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(receivedPath) ?? ".");
            File.WriteAllText(receivedPath, actual);
            output.WriteLine("Received table: " + receivedPath);
        }

        Assert.Equal(expected, actual);
    }
}
