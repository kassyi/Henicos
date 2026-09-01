using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_0008_LinqProhibitionSadPathTests
{
    [Fact]
    [Trait("FeatureId", "GSH-0008")]
    [Trait("Category", "SadPath")]
    public void Test_GSH0008_LinqProhibition_ReportsDiagnostic_OnQueryExpressions()
    {
        var diagnostics = MegaCompilationCache.GetAnalyzerDiagnosticsForFile("GSH-0008_LinqSadPath.cs");
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
    public void Test_GSH0008_LinqProhibition_GeneratorReportsDiagnostics()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("GSH-0008_LinqSadPath.cs");
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.LinqNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}
