
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests : TranspilerTestBase
{
    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var driver = await TestGenerator("Generator/GEN-05_SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var driver = await TestGenerator("GEN-06_ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/GEN-07_UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
