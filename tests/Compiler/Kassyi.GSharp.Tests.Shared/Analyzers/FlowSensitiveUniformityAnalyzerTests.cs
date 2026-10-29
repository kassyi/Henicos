#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// GSH0037 follows the control flow graph (#447, #446): the value of a local at a branch is the one that reaches it,
/// a deconstruction assigns each element, and a barrier is divergent when a thread-variant branch decides whether a
/// thread reaches it while the threads that skip it reach another split point (a loop, <c>continue</c>, <c>break</c>,
/// a later barrier). A grid barrier is also divergent on a branch on the block index (B2).
/// </summary>
[Category(TestCategory.Compiler)]
public class FlowSensitiveUniformityAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarriers_FollowTheControlFlowGraph()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/FlowSensitiveUniformityKernels.cu.cs");

        // GSH0083 for every divergent branch. 80: loop-carried value. 100: deconstructed element. 114: while with a thread-variant count. 128: switch
        // section. 143 / 144: right operand of && and branch of ?:. 161: continue. 177: break. 197: grid barrier on the
        // block index. 202: grid barrier on the thread index. 245: a local assigned on one branch, read after the join
        Assert.Equal(
            [
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 80, 17, 80, 34),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 100, 13, 100, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 114, 13, 114, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 128, 17, 128, 34),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 143, 43, 143, 59),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 144, 43, 144, 59),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 161, 13, 161, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 177, 13, 177, 30),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 197, 13, 197, 27),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 202, 13, 202, 27),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 214, 13, 214, 30),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 226, 13, 226, 31),
                (GSharpDiagnostics.LaunchDependentDivergentBarrier.Id, 245, 13, 245, 30)
            ],
            actual);
    }

    // The shapes the GSH0037 tests used before, and early exits: the threads that skip the barrier only leave the
    // kernel. GSH0084 (warning) at the condition that sends them out, or at the call whose callee they leave early;
    // a uniform condition (UniformReturnKernel) and a device function body are not reported
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ExitOnlyBarriers_ReportGSH0084()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ExitOnlyBarrierKernels.cu.cs");

        Assert.Equal(
            [
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 16, 13, 16, 31),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 26, 13, 26, 38),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 40, 17, 40, 34),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 61, 13, 61, 22),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 72, 37, 72, 46),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 92, 9, 92, 26),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 105, 13, 105, 31),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 122, 21, 122, 39),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 130, 13, 130, 31),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 140, 13, 140, 30),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 150, 13, 150, 43),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 166, 17, 166, 43),
                (GSharpDiagnostics.BarrierAfterDivergentExit.Id, 205, 9, 205, 26)
            ],
            actual);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_BarrierAfterDivergentExit_IsAWarningWithTheBarrier()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/BarrierUsage/ExitOnlyBarrierKernels.cu.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));

        // The early return of EarlyReturnKernel points at the barrier it skips
        var earlyReturn = diagnostics.Single(static d => d.Location.GetLineSpan().StartLinePosition.Line == 149);
        Assert.Equal(154, Assert.Single(earlyReturn.AdditionalLocations).GetLineSpan().StartLinePosition.Line);
        Assert.Contains("without reaching 'SyncThreads'", earlyReturn.GetMessage(), StringComparison.Ordinal);

        // The call to a callee that lets threads leave early names the callee once for each part
        var call = diagnostics.Single(static d => d.Location.GetLineSpan().StartLinePosition.Line == 204);
        Assert.StartsWith("Some threads leave 'SyncWithin' early without reaching a barrier inside 'SyncWithin',", call.GetMessage(),
            StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>> RunAsync(
        string relativeTestCasePath)
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(relativeTestCasePath, s_analyzer).GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }
}
