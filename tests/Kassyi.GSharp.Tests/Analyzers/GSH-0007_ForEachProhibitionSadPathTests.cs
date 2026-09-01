using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_0007_ForEachProhibitionSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "GSH-0007")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH0007_ForEachProhibition_ReportsDiagnostic_OnAllForEachVariants()
    {
        var path = GetTestCasePath("GSH-0007_ForEachSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new GpuKernelAnalyzer();
        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        var foreachDiagnostics = diagnostics.Where(d => d.Id == GSharpDiagnostics.ForeachNotSupported.Id).ToList();

        // Expect diagnostics for: Basic foreach (1), Nested foreach (2), Foreach over Span (1), Foreach in local function (1) -> total 5 foreach statements
        Assert.Equal(5, foreachDiagnostics.Count);
        foreach (var diag in foreachDiagnostics)
        {
            Assert.Equal(GSharpDiagnostics.ForeachNotSupported.Id, diag.Id);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        }
    }

    [Fact]
    [Trait("FeatureId", "GSH-0007")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH0007_ForEachProhibition_GeneratorReportsDiagnostics()
    {
        var path = GetTestCasePath("GSH-0007_ForEachSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == GSharpDiagnostics.ForeachNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}
