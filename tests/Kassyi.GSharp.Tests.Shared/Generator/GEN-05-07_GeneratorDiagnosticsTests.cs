
namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var driver = await RoslynTestUtility.TestGenerator("Generator/GEN-05_SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

#if GSHARP_CPU
        var dialectName = "Cpu";
#else
        var dialectName = "Cuda";
#endif
        await Verifier.Verify(driver).UseMethodName($"TestSadPathDiagnostics_{dialectName}");
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var driver = await RoslynTestUtility.TestGenerator("GEN-06_ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

#if GSHARP_CPU
        var dialectName = "Cpu";
#else
        var dialectName = "Cuda";
#endif
        await Verifier.Verify(driver).UseMethodName($"TestComprehensiveSpecKernelGeneratedSource_{dialectName}");
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var driver = await RoslynTestUtility.TestGenerator("Generator/GEN-07_UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

#if GSHARP_CPU
        var dialectName = "Cpu";
#else
        var dialectName = "Cuda";
#endif
        await Verifier.Verify(driver).UseMethodName($"Test_UnsupportedFeatures_ReportsDiagnostics_{dialectName}");
    }
}
