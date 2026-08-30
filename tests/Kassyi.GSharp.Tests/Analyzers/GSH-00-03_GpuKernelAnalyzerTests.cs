using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_00_03_GpuKernelAnalyzerTests
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

    private static async Task<ImmutableArray<Diagnostic>> GetGeneratorDiagnosticsFromTestNameAsync(string testName)
    {
        var path = GetTestCasePath(testName);
        var source = await File.ReadAllTextAsync(path);
        var (_, _, diagnostics) = Helpers.RoslynTestUtility.RunGenerator(source);
        return diagnostics;
    }


    [Fact]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH01_StringTernary_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetGeneratorDiagnosticsFromTestNameAsync("GSH-01_StringTernary_InGpuKernel");
        diagnostics.AssertContainsError(GSharpDiagnostics.StringNotSupported.Id);
    }


    [Fact]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH02_LinqQuery_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-02_LinqQuery_InGpuKernel");
        diagnostics.AssertContainsError(GSharpDiagnostics.LinqNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_GSH03_ImplicitVar_WithoutSuffix_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GSH-03_ImplicitVar_WithoutSuffix_InGpuKernel");
        diagnostics.AssertContainsError(GSharpDiagnostics.ImplicitVarNeedsSuffix.Id);
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
