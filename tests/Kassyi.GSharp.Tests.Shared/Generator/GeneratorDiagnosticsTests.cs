
namespace Kassyi.GSharp.Tests.Shared.Generator;

public class GeneratorDiagnosticsTests : TranspilerTestBase
{
    [Fact]
    public async Task TestSadPathDiagnostics()
    {
        var driver = await TestGenerator("Generator/SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var driver = await TestGenerator("ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
