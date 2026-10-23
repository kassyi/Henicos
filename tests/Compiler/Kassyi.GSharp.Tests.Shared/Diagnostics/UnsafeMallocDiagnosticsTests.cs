using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>GSH0029 comes from the kernel analyzer, for the call of <c>Gpu.Unsafe.Malloc</c> only.</summary>
[Category(TestCategory.Compiler)]
public class UnsafeMallocDiagnosticsTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_Malloc_ReportsGsh0029OnTheCallInKernelAndDeviceFunction(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsafeMallocWarning.cu.cs", dialect);

        // The calls of Free and the suppressed kernel report nothing
        Assert.Equal(
            [(Analyzer, 10, 23, 10, 44), (Analyzer, 18, 28, 18, 48)],
            diagnostics.Ranges(GSharpDiagnostics.ManualUnmanagedAllocation.Id));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ManualUnmanagedAllocation_IsAWarning()
    {
        Assert.Equal(DiagnosticSeverity.Warning, GSharpDiagnostics.ManualUnmanagedAllocation.DefaultSeverity);
    }
}
