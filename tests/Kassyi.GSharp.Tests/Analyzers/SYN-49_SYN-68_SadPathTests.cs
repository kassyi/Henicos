using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_49_SYN_68_SadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-49")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN49_UnsupportedInterlockedOverload_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-49_UnsupportedInterlockedOverload.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "SYN-68")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN68_UnsupportedMathMethod_ReportsDiagnosticAndPreventsCrash()
    {
        var path = GetTestCasePath("SYN-68_UnsupportedMathMethod.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = GpuTranspilerTestRunner.RunTranspiler(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        diagnostics.AssertContainsError("GSH0003");
    }
}

