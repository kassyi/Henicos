using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_23_31_TypeSystemAndStructSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "GEN-23")]
    [Trait("Category", "SadPath")]
    public async Task Test_GEN23_InvalidFixedTarget_ReportsDiagnostic()
    {
        var path = GetTestCasePath("GEN-23_InvalidFixedTargetSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS8385" || d.Id.StartsWith("GSH")));
    }

    [Fact]
    [Trait("FeatureId", "GEN-30")]
    [Trait("Category", "SadPath")]
    public async Task Test_GEN30_InvalidInlineArrayLength_ReportsDiagnostic()
    {
        var path = GetTestCasePath("GEN-30_InvalidInlineArrayLengthSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS9167" || d.Id.StartsWith("GSH")));
    }

    [Fact]
    [Trait("FeatureId", "GEN-31")]
    [Trait("Category", "SadPath")]
    public async Task Test_GEN31_CircularStructDependency_ReportsDiagnostic()
    {
        var path = GetTestCasePath("GEN-31_CircularStructSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS0523" || d.Id.StartsWith("GSH")));
    }
}
