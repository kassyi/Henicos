using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_0007_ForEachProhibitionSadPathTests
{
    [Fact]
    [Trait("FeatureId", "GSH-0007")]
    [Trait("Category", "SadPath")]
    public void Test_GSH0007_ForEachProhibition_ReportsDiagnostic_OnAllForEachVariants()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GSH-0007_ForEachSadPath.cs");
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
    public void Test_GSH0007_ForEachProhibition_GeneratorReportsDiagnostics()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("GSH-0007_ForEachSadPath.cs");
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.ForeachNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}
