#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Builds the IR of every GPU method of the generator snapshot inputs (<c>TestCases/</c>) and of the differential
/// kernels (#1561): the builder never throws, every tree keeps the invariants, and what stays opaque, counted per
/// method by node kind, reason, operation kind and syntax kind, matches <c>IrOpaqueCoverage.expected.yml</c>. A change
/// that makes more constructs opaque fails; one that builds more of them updates the file, which shows how far the IR
/// reaches.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrCoverageTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrOpaqueCoverage.expected.yml";

    private static readonly string[] s_header =
    [
        "The nodes the IR builder keeps opaque, per GPU method of the IR corpus (the files of TestCases/ and the",
        "differential kernels). IrCoverageTests.EveryGpuMethod_BuildsAndKeepsTheInvariants builds the list again on",
        "every run and fails on any difference; a received file goes to tmp/. The test output prints the total of each",
        "category. A pull request that adds or changes a kernel changes the entries of that method only.",
        "",
        "  file:      the source file, relative to tests/Compiler/Kassyi.GSharp.Tests.Shared",
        "  method:    the containing type and the method",
        "  node:      expression or statement",
        "  reason:    why the node stays opaque (IrOpaqueReason)",
        "  operation: the IOperation kind of the node, - when it has none",
        "  syntax:    the syntax kind of the node",
        "  detail:    for an object creation, what keeps it opaque (a class, a nested member initializer, a collection",
        "             initializer)",
        "  count:     how many such nodes the method has"
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_BuildsAndKeepsTheInvariants()
    {
        var stopwatch = Stopwatch.StartNew();
        var opaque = new IrCorpusCounts();
        var violations = new ConcurrentBag<string>();
        var built = 0;

        var (fileCount, differentialCount) = IrCoverageInputs.ForEach((compilation, trees) =>
            Interlocked.Add(ref built, IrCoverage.BuildAll(compilation, trees, opaque, violations)));

        output.WriteLine($"{fileCount} test case files, {differentialCount} differential files, " +
                         $"{built} methods in {stopwatch.ElapsedMilliseconds} ms, {opaque.Sum} opaque nodes:");
        foreach (var total in opaque.Totals())
        {
            output.WriteLine(total);
        }

        Assert.True(built > 500, $"Only {built} GPU methods were built.");
        Assert.True(violations.IsEmpty,
            string.Join("\n", violations.OrderBy(static v => v, StringComparer.Ordinal).Take(100)));
        IrExpectedFiles.AssertMatches(ExpectedFile, opaque.Render(s_header), output);
    }
}
