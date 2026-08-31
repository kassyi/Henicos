using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GBI_30_34_BCL_01_03_GbiAndBclSadPathTests
{
    private static ImmutableArray<Diagnostic> GetAllDiagnosticsFromTestName(string testName)
    {
        return MegaCompilationCache.GetAllDiagnosticsForFile($"{testName}.cu.cs");
    }

    [Fact]
    [Trait("FeatureId", "GBI-30")]
    [Trait("Category", "SadPath")]
    public void Test_GBI30_HostPropertyAccess_ReportsDiagnostic()
    {
        var diagnostics = GetAllDiagnosticsFromTestName("GBI-30_HostPropertyAccess_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GBI-31")]
    [Trait("Category", "SadPath")]
    public void Test_GBI31_DynamicWarpDivergentSync_ReportsDiagnostic()
    {
        var diagnostics = GetAllDiagnosticsFromTestName("GBI-31_DynamicWarpDivergentSync_InGpuKernel");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.SyncThreadsNotAllowedInDivergentPath.Id && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "GBI-34")]
    [Trait("Category", "SadPath")]
    public void Test_GBI34_DynamicStringInlinePtx_ReportsDiagnostic()
    {
        var diagnostics = GetAllDiagnosticsFromTestName("GBI-34_DynamicStringInlinePtx_InGpuKernel");
        Assert.Contains(diagnostics, d => (d.Id == GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral.Id || d.Id == GSharpDiagnostics.StringNotSupported.Id) && d.Severity == DiagnosticSeverity.Error);
    }
}
