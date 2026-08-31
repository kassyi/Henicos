using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GpuBigIntExprAnalyzerTests
{
    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public void LocalDeclaration_OfGpuBigIntExpr_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GBI-26_LocalDeclaration.cs");
        diagnostics.AssertContains(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-27")]
    public void Assignment_ToGpuBigIntExpr_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GBI-27_Assignment.cs");
        diagnostics.AssertContains(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-28")]
    public void LegacyNamespace_Declaration_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GBI-28_LegacyNamespace.cs");
        diagnostics.AssertContains(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public void MultipleDeclarations_InSingleStatement_EdgeCase()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GBI-26_MultipleDeclarations.cs");
        var count = diagnostics.Count(d => d.Id == GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public void MalformedSyntax_ShouldNotCrash_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GBI-26_MalformedSyntax.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.GpuBigIntExprDirectAssignment.Id); 
    }
}

