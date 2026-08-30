
namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var path = Path.Combine(GpuAnalyzerTestRunner.GetTestCasesDirectory(), "Generator",
            "GEN-05_SadPathDiagnostics.cu.cs");
        var source = await File.ReadAllTextAsync(path);
        var (driver, _, _) = GpuTranspilerTestRunner.RunTranspiler(source);

        await Verifier.Verify(driver).UseMethodName("TestSadPathDiagnostics");
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var path = Path.Combine(GpuAnalyzerTestRunner.GetTestCasesDirectory(), "GEN-06_ComprehensiveSpecKernel.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = GpuTranspilerTestRunner.RunTranspiler(source);

        await Verifier.Verify(driver).UseMethodName("TestComprehensiveSpecKernelGeneratedSource");
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var path = Path.Combine(GpuAnalyzerTestRunner.GetTestCasesDirectory(), "Generator",
            "GEN-07_UnsupportedFeatures.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = GpuTranspilerTestRunner.RunTranspiler(source);

        await Verifier.Verify(driver).UseMethodName("Test_UnsupportedFeatures_ReportsDiagnostics");
    }
}

