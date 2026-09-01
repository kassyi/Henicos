using System.Linq;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_13_GEN_29_SadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-13")]
    [Trait("FeatureId", "GSH-04")]
    [Trait("Category", "SadPath")]
    public void Test_SYN13_DynamicStackalloc_ReportsDiagnostic()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-13_DynamicStackAllocSadPath.cs");
        diagnostics.AssertContainsError("GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "GEN-29")]
    [Trait("Category", "SadPath")]
    public void Test_GEN29_DynamicSharedMemory_SupportedOrEmitsNoUnexpectedErrors()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("GEN-29_DynamicSharedMemorySadPath.cs");
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
