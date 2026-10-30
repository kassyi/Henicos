using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The CPU dialects return every lane of the warp that exists in the block from <c>Warp.ActiveMask()</c>; the GPU returns
/// only the lanes that reach the call together. Under a condition that may differ between the threads the two differ, so
/// the analyzer reports such a call as GSH0101 on the CPU dialects and stays silent on CUDA (#789).
/// </summary>
[Category(TestCategory.Compiler)]
public class WarpActiveMaskDivergenceDiagnosticsTests
{
    private const string TestCase = "Analyzers/WarpActiveMaskDivergentSadPath.cs";

    // An if, ?:, for and && with a thread-dependent condition, an if in a device function, a call after an earlier
    // thread-dependent return (the bounds guard) or break of the same body, and an if on a device function parameter; not
    // the call every thread reaches, the one under a uniform condition, the body of Gpu.GridStrideLoop, a call after a
    // loop that every thread leaves, after a return of a local function, or after a return under a uniform condition
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ActiveMaskUnderThreadDependentCondition_OnCpu_ReportsGsh0101AtTheCall()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Equal(
            [
                (Analyzer, 16, 20, 16, 37),
                (Analyzer, 24, 29, 24, 46),
                (Analyzer, 27, 20, 27, 37),
                (Analyzer, 30, 29, 30, 46),
                (Analyzer, 43, 20, 43, 37),
                (Analyzer, 61, 16, 61, 33),
                (Analyzer, 75, 20, 75, 37),
                (Analyzer, 113, 20, 113, 37)
            ],
            diagnostics.Ranges(GSharpDiagnostics.WarpActiveMaskInDivergentCode.Id));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ActiveMaskUnderThreadDependentCondition_OnCuda_ReportsNothing()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cuda");

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.WarpActiveMaskInDivergentCode.Id));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ActiveMaskUnderThreadDependentCondition_IsAWarning()
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, "Cpu");

        Assert.Equal(DiagnosticSeverity.Warning, GSharpDiagnostics.WarpActiveMaskInDivergentCode.DefaultSeverity);
        Assert.Single(diagnostics.Ranges(GSharpDiagnostics.WarpActiveMaskInDivergentCode.Id,
            "Warp.ActiveMask() in 'MaskOfFirstLanes' is reached only by the threads"));
    }
}
