
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class GpuBigIntExprAnalyzerTests
:  TranspilerTestBase{
    private static readonly GpuBigIntExprAnalyzer s_analyzer = new();

    [Fact]
    public async Task LocalDeclaration_OfGpuBigIntExpr_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/LocalDeclaration.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment);
    }

    [Fact]
    public async Task MultipleDeclarations_InSingleStatement_EdgeCase()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/MultipleDeclarations.cs", s_analyzer)
                                                .GetDiagnosticsAsync();
        var count = diagnostics.Count(d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MalformedSyntax_ShouldNotCrash_SadPath()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/MalformedSyntax.cs", s_analyzer)
                               .ReportsErrorsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment);
    }

    [Fact]
    public async Task Assignment_ToGpuBigIntExpr_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/Assignment.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment);
    }

    [Fact]
    public async Task LegacyNamespace_Declaration_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/GpuBigIntExpr/LegacyNamespace.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(GSharpDiagnostics.GpuBigIntExprDirectAssignment);
    }
}
