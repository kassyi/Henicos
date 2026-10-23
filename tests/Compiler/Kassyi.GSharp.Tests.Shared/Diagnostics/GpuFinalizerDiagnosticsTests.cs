using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports a finalizer that carries <c>[GpuKernel]</c> or <c>[GpuDeviceFunction]</c> as GSH0003 at
/// <c>~Name</c>; the finalizer of a class with kernels, without the attribute, is host code.
/// </summary>
[Category(TestCategory.Compiler)]
public class GpuFinalizerDiagnosticsTests
{
    // Line 11: [GpuKernel]; line 20: [GpuDeviceFunction]; HostFinalizerKernels (line 35) is not reported
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FinalizerMarkedAsGpuCode_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/GpuFinalizer.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 11, 5, 11, 26),
                (Analyzer, 20, 5, 20, 29)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'DestructorDeclaration'"));
        Assert.DoesNotContain(diagnostics, static d => d.Origin == DiagnosticOrigin.Generator);
    }
}
