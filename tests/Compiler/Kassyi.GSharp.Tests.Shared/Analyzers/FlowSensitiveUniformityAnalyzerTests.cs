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

        // 80: loop-carried value. 100: deconstructed element. 114: while with a thread-variant count. 128: switch
        // section. 143 / 144: right operand of && and branch of ?:. 161: continue. 177: break. 197: grid barrier on the
        // block index. 202: grid barrier on the thread index. 245: a local assigned on one branch, read after the join
        Assert.Equal(
            [
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 80, 17, 80, 34),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 100, 13, 100, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 114, 13, 114, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 128, 17, 128, 34),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 143, 43, 143, 59),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 144, 43, 144, 59),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 161, 13, 161, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 177, 13, 177, 30),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 197, 13, 197, 27),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 202, 13, 202, 27),
                (GSharpDiagnostics.DivergentBarrierProhibited.Id, 245, 13, 245, 30)
            ],
            actual);
    }

    // The shapes the GSH0037 tests used before: the threads that skip the barrier only leave the kernel or the device
    // function. They are exit only, not GSH0037; the stage that adds the exit-only warning replaces this empty list
    // with its coordinates.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_ExitOnlyBarriers_AreNotGSH0037()
    {
        var actual = await RunAsync("Analyzers/BarrierUsage/ExitOnlyBarrierKernels.cu.cs");

        Assert.Empty(actual);
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
