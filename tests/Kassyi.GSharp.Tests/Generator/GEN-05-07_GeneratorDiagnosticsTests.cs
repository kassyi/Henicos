
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var driver = await RoslynTestUtility.TestGenerator("Generator/GEN-05_SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

        await Verifier.Verify(driver).UseMethodName("TestSadPathDiagnostics");
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var driver = await RoslynTestUtility.TestGenerator("GEN-06_ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

        await Verifier.Verify(driver).UseMethodName("TestComprehensiveSpecKernelGeneratedSource");
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var driver = await RoslynTestUtility.TestGenerator("Generator/GEN-07_UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verifier.Verify(driver).UseMethodName("Test_UnsupportedFeatures_ReportsDiagnostics");
    }
}
