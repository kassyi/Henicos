using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>foreach (ref ...)</c> whose loop holds a split point is GSH0039 at its variable (#1657): a block barrier, a
/// warp shuffle, <c>Gpu.SyncWarp</c>, <c>Gpu.SyncGrid</c> or a call that may suspend, also through a second function;
/// a split point outside the loop, or in a local function of the loop, is not the loop's.
/// </summary>
[Category(TestCategory.Compiler)]
public class RefForEachAroundSplitDiagnosticsTests
{
    private const string TestCase = "Diagnostics/RefForEachAroundSplit.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefForEach_LoopWithASplitPoint_ReportsGsh0039AtTheVariable(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Shuffle, SyncWarp, SyncGrid, ref readonly, a barrier in a nested loop, a suspending call through a second
        // function, the second of two loops, and the outer of two nested loops. Not reported: the plain foreach and
        // the loop whose only barrier is in a local function (GSH0037 reports that barrier)
        Assert.Equal(
            [
                (Analyzer, 29, 26, 29, 27),
                (Analyzer, 39, 26, 39, 27),
                (Analyzer, 50, 26, 50, 27),
                (Analyzer, 61, 35, 61, 36),
                (Analyzer, 72, 26, 72, 27),
                (Analyzer, 87, 26, 87, 27),
                (Analyzer, 104, 26, 104, 32),
                (Analyzer, 116, 26, 116, 31)
            ],
            diagnostics.Ranges(GSharpDiagnostics.CrossBarrierRefStructProhibited.Id));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RefForEach_LoopWithASplitPoint_IsNotAlsoReportedByTheGenerator()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        // The held variable of such a loop is GSH0039; GSH0003 is left for a held ref local that it does not report
        Assert.Empty(diagnostics.Ranges(
            GSharpDiagnostics.UnsupportedSyntax.Id,
            GpuCodeConstraints.RefLocalHeldByCpuStateMachine
        ));
    }
}
