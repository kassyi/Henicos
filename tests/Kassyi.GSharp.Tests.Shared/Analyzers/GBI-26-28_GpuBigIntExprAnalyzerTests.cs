
namespace Kassyi.GSharp.Tests;

public class GpuBigIntExprAnalyzerTests
:  TranspilerTestBase{
    private static readonly GpuBigIntExprAnalyzer s_analyzer = new();

    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    public async Task LocalDeclaration_OfGpuBigIntExpr_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/GBI-26_LocalDeclaration.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MultipleDeclarations_InSingleStatement_EdgeCase()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/GBI-26_MultipleDeclarations.cs", s_analyzer)
                                                .GetDiagnosticsAsync();
        var count = diagnostics.Count(d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
        Assert.Equal(2, count);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    public async Task MalformedSyntax_ShouldNotCrash_SadPath()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/GBI-26_MalformedSyntax.cs", s_analyzer)
                               .ReportsErrorsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-27")]
    public async Task Assignment_ToGpuBigIntExpr_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/GBI-27_Assignment.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-28")]
    public async Task LegacyNamespace_Declaration_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/GBI-28_LegacyNamespace.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }
}
