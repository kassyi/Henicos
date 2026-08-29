using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_0008_LinqProhibitionSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "GSH-0008")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH0008_LinqProhibition_ReportsDiagnostic_OnQueryExpressions()
    {
        var path = GetTestCasePath("GSH-0008_LinqSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new GpuKernelAnalyzer();
        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        var linqDiagnostics = diagnostics.Where(d => d.Id == GSharpDiagnostics.LinqNotSupported.Id).ToList();

        // Expect diagnostics for: Simple query (1), Complex/Nested query (1) -> total at least 2 GSH0008 diagnostics
        Assert.True(linqDiagnostics.Count >= 2, $"Expected at least 2 GSH0008 diagnostics, but got {linqDiagnostics.Count}");
        foreach (var diag in linqDiagnostics)
        {
            Assert.Equal(GSharpDiagnostics.LinqNotSupported.Id, diag.Id);
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        }
    }

    [Fact]
    [Trait("FeatureId", "GSH-0008")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH0008_LinqProhibition_GeneratorReportsDiagnostics()
    {
        var path = GetTestCasePath("GSH-0008_LinqSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        Assert.NotNull(driver);
        Assert.NotNull(compilation);

        // LINQ query expressions or method chaining in kernel emit error diagnostics (e.g., GSH0008 or GSH0003)
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == GSharpDiagnostics.LinqNotSupported.Id || d.Severity == DiagnosticSeverity.Error);
    }
}
