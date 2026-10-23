using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>The analyzer reports the diagnostics of the project's dialect, once; the generator reports none of them.</summary>
[Category(TestCategory.Compiler)]
public class DialectDiagnosticsTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Generator/DialectDiagnostics.cu.cs");

    // The C++ dialect's own failures used to be dropped, so a CPU build said nothing about the replaced call
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CpuDialect_ReportsInlinePtxOncePerCall()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cpu");

        var inlinePtx = diagnostics.Where(static d => d.Id == GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id).ToList();
        Assert.Equal(2, inlinePtx.Count);
        Assert.All(inlinePtx, static d => Assert.Equal(DiagnosticOrigin.Analyzer, d.Origin));
        Assert.Equal([11, 18], inlinePtx.Select(static d => d.StartLine));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CudaDialect_DoesNotReportTheCppDialectsFailures()
    {
        var diagnostics = await KernelDiagnostics.RunAsync(s_source, "Cuda");

        Assert.DoesNotContain(diagnostics, static d => d.Id == GSharpDiagnostics.InlinePtxNotSupportedByDialect.Id);
    }
}
