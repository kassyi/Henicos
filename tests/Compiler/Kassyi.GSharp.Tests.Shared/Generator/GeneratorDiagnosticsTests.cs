using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public class GeneratorDiagnosticsTests : TranspilerTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task GeneratorDiagnostics_InvalidSyntax_ReportsExpectedDiagnostics()
    {
        var driver = await TestGenerator("Generator/SadPathDiagnostics.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task ComprehensiveSpecKernel_GeneratedSource_EmitsExpectedCode()
    {
        var driver = await TestGenerator("ComprehensiveSpecKernel.cu.cs")
                                            .GetDriverAsync();

        // The hash of the preludes and the internal names changes with every edit of a prelude, the format version with
        // every change of the record layout, so the values this compiler writes become placeholders
        await Verify(driver).ScrubLinesWithReplace(static line => line.Replace(
            $"GpuDeviceExportAttribute({DeviceExportRecord.CurrentFormatVersion}, @\"{DeviceExportRecord.CurrentPreludeHash}\"",
            "GpuDeviceExportAttribute({FormatVersion}, @\"{PreludeHash}\"",
            StringComparison.Ordinal));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task UnsupportedFeatures_KernelDeclaration_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }
}
