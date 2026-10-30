
namespace Kassyi.GSharp.Tests.Shared.Generator;

public class GeneratorDiagnosticsTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task TestSadPathDiagnostics()
    {
        var driver = await TestGenerator("Generator/SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var driver = await TestGenerator("ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

        // The hash of the preludes and the internal names changes with every edit of a prelude, the format version with
        // every change of the record layout
        await Verify(driver).ScrubLinesWithReplace(static line =>
            System.Text.RegularExpressions.Regex.Replace(line, "GpuDeviceExportAttribute\\([0-9]+, @\"[0-9a-f]{16}\"",
                "GpuDeviceExportAttribute({FormatVersion}, @\"{PreludeHash}\""));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
