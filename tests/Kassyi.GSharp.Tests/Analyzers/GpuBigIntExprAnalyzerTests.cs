using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests;

public class GpuBigIntExprAnalyzerTests
{
    private static string GetTestCasePath(string testName, [CallerFilePath] string callerFilePath = "")
    {
        var projectDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, ".."));
        return Path.Combine(projectDir, "TestCases", "Analyzers", "GpuBigIntExpr", $"{testName}.cs");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsFromTestNameAsync(string testName)
    {
        var path = GetTestCasePath(testName);
        var source = await File.ReadAllTextAsync(path);
        var analyzer = new GpuBigIntExprAnalyzer();
        return await Helpers.RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task LocalDeclaration_OfGpuBigIntExpr_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GBI-26_LocalDeclaration");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-27")]
    public async Task Assignment_ToGpuBigIntExpr_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GBI-27_Assignment");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-28")]
    public async Task LegacyNamespace_Declaration_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GBI-28_LegacyNamespace");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MultipleDeclarations_InSingleStatement_EdgeCase()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GBI-26_MultipleDeclarations");
        var count = diagnostics.Count(d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MalformedSyntax_ShouldNotCrash_SadPath()
    {
        var diagnostics = await GetAnalyzerDiagnosticsFromTestNameAsync("GBI-26_MalformedSyntax");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id && d.Severity == DiagnosticSeverity.Error); 
    }
}
