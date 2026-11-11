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
        // every change of the record layout
        await Verify(driver).ScrubLinesWithReplace(ScrubGpuDeviceExportAttribute);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public async Task UnsupportedFeatures_KernelDeclaration_ReportsDiagnostics()
    {
        var driver = await TestGenerator("Generator/UnsupportedFeatures.cu.cs")
                                            .GetDriverAsync();

        await Verify(driver);
    }

    private static string ScrubGpuDeviceExportAttribute(string line)
    {
        const string targetPrefix = "GpuDeviceExportAttribute(";
        int idx = line.IndexOf(targetPrefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            return line;
        }

        int start = idx + targetPrefix.Length;
        int commaIdx = line.IndexOf(',', start);
        if (commaIdx < 0)
        {
            return line;
        }

        for (int i = start; i < commaIdx; i++)
        {
            if (!char.IsAsciiDigit(line[i]))
            {
                return line;
            }
        }

        const string hashPrefix = " @\"";
        int hashStart = commaIdx + 1;
        if (hashStart + hashPrefix.Length > line.Length ||
            !line.AsSpan(hashStart, hashPrefix.Length).SequenceEqual(hashPrefix.AsSpan()))
        {
            return line;
        }

        int hexStart = hashStart + hashPrefix.Length;
        if (hexStart + 17 > line.Length)
        {
            return line;
        }

        for (int i = 0; i < 16; i++)
        {
            if (!char.IsAsciiHexDigit(line[hexStart + i]))
            {
                return line;
            }
        }

        if (line[hexStart + 16] != '"')
        {
            return line;
        }

        return line[..idx] + "GpuDeviceExportAttribute({FormatVersion}, @\"{PreludeHash}\"" + line[(hexStart + 17)..];
    }
}
