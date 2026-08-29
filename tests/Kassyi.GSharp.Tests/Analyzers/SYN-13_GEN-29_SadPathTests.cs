using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_13_GEN_29_SadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_SYN13_DynamicStackalloc_ReportsDiagnostic()
    {
        var path = GetTestCasePath("SYN-13_DynamicStackAllocSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "GEN-29")]
    [Trait("Category", "SadPath")]
    public async Task Test_GEN29_DynamicSharedMemory_SupportedOrEmitsNoUnexpectedErrors()
    {
        var path = GetTestCasePath("GEN-29_DynamicSharedMemorySadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
