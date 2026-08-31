using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_17_TargetTypedNewSadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-17")]
    [Trait("Category", "SadPath")]
    public void Test_SYN17_UnresolvedTargetTypedNew_ReportsDiagnosticAndPreventsCrash()
    {
        var allDiagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-17_UnresolvedTargetTypedNew.cs");

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && (d.Id.StartsWith("CS") || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id));
    }
}

