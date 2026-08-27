
namespace Kassyi.GSharp.Tests.Analyzers;

public class GpuKernelAnalyzerTests
{
    private static string GetTestCasePath(string testName, [CallerFilePath] string callerFilePath = "")
    {
        var projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, ".."));
        return Path.Combine(projectDir, "TestCases", "Analyzers", "GpuKernel", $"{testName}.cu.cs");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsFromTestNameAsync(string testName)
    {
        var path = GetTestCasePath(testName);
        var source = await File.ReadAllTextAsync(path);
        var analyzer = new GpuKernelAnalyzer();
        return await Helpers.RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);
    }

    [Fact]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH01_Foreach_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-01_Foreach_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.ForeachNotSupported.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH02_LinqQuery_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-02_LinqQuery_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.LinqNotSupported.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH03_ImplicitVar_WithoutSuffix_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-03_ImplicitVar_WithoutSuffix_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.ImplicitVarNeedsSuffix.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GSH-00")]
    public async Task Test_GSH00_ValidSyntax_InGpuKernel_DoesNotReportDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-00_ValidSyntax_InGpuKernel");
        Assert.Empty(diagnostics.Where(d => d.Id == GSharpDiagnostics.ForeachNotSupported.Id || 
                                            d.Id == GSharpDiagnostics.LinqNotSupported.Id || 
                                            d.Id == GSharpDiagnostics.ImplicitVarNeedsSuffix.Id));
    }
}
