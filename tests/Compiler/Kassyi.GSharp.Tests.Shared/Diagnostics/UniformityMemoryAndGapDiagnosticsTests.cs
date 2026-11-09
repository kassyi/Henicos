using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The uniformity analysis records a write of a field of an array, span or shared-memory element as a write of that
/// element (#1272), and takes a gap of a <c>Gpu.SyncGrid(body)</c> for a point where the threads that skip a divergent
/// block barrier wait while the others wait at the barrier (#982).
/// </summary>
[Category(TestCategory.Compiler)]
public class UniformityMemoryAndGapDiagnosticsTests
{
    private const string FieldWrites = "Diagnostics/ElementFieldWrites.cu.cs";
    private const string GapBarrier = "Diagnostics/SyncGridGapBarrier.cu.cs";

    // Written field by field, then read under the condition of a barrier: GSH0083 as for a write of the element. A
    // barrier between the write and the read (FieldWriteThenBarrierKernel) and a field of a local struct
    // (LocalFieldKernel) are not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FieldOfAnElementWritten_ReportsGsh0083(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(FieldWrites, dialect);

        Assert.Equal(
            [
                (Analyzer, 26, 13, 26, 30),
                (Analyzer, 39, 13, 39, 30),
                (Analyzer, 52, 13, 52, 30),
                (Analyzer, 65, 13, 65, 30),
                (Analyzer, 79, 13, 79, 30),
                (Analyzer, 93, 13, 93, 30),
                (Analyzer, 137, 13, 137, 30)
            ],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.BarrierAfterDivergentExit.Id));
    }

    // The threads that skip the block barrier meet the others at the grid synchronization of the next gap: GSH0083,
    // not GSH0084 (no thread leaves the kernel)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DivergentBarrierBeforeASyncGridGap_ReportsGsh0083(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(GapBarrier, dialect);

        Assert.Equal(
            [(Analyzer, 17, 17, 17, 34)],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
        Assert.DoesNotContain(diagnostics.Ranges(GSharpDiagnostics.BarrierAfterDivergentExit.Id),
            static range => range.StartLine is 15 or 17);
    }
}
