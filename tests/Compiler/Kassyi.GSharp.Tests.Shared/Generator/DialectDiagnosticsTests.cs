using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>The analyzer reports the diagnostics of the project's dialect, once; the generator reports none of them.</summary>
[Category(TestCategory.Compiler)]
public class DialectDiagnosticsTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Generator/DialectDiagnostics.cu.cs");

    // The C++ dialect's own failures used to be dropped, so a CPU build said nothing about the replaced call. Each call is
    // reported once at the call: the device function's (line 11) and the kernel's, whose C++ output is lowered (line 18).
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CpuDialect_ReportsInlinePtxOncePerCall()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cpu");

        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 11, 13, 11, 34),
                (DiagnosticOrigin.Analyzer, 18, 13, 18, 34)
            ],
            diagnostics.Ranges(GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CudaDialect_DoesNotReportTheCppDialectsFailures()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cuda");

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id));
    }
}
