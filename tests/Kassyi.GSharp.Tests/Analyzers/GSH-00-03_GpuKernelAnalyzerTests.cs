using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_00_03_GpuKernelAnalyzerTests
{
    [Fact]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "SadPath")]
    public void Test_GSH01_Foreach_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GSH-01_Foreach_InGpuKernel.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.ForeachNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "GSH-01")]
    [Trait("Category", "SadPath")]
    public void Test_GSH01_StringTernary_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("GSH-01_StringTernary_InGpuKernel.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.StringNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "SadPath")]
    public void Test_GSH02_LinqQuery_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GSH-02_LinqQuery_InGpuKernel.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.LinqNotSupported.Id);
    }

    [Fact]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "SadPath")]
    public void Test_GSH02_TupleWithReferenceType_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("GSH-02_TupleWithReferenceTypeSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    [Trait("FeatureId", "GSH-03")]
    [Trait("Category", "SadPath")]
    public void Test_GSH03_ImplicitVar_WithoutSuffix_InGpuKernel_ReportsDiagnostic()
    {
        var diagnostics =
            MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GSH-03_ImplicitVar_WithoutSuffix_InGpuKernel.cu.cs");
        diagnostics.AssertContainsError(GSharpDiagnostics.ImplicitVarNeedsSuffix.Id);
    }
}

