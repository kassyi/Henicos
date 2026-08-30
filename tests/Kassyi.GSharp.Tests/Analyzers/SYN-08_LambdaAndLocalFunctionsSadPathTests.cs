using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_08_LambdaAndLocalFunctionsSadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-08")]
    [Trait("Category", "SadPath")]
    public void Test_SYN08_DelegateEscape_ReportsDiagnosticAndPreventsCrash()
    {
        var allDiagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-08_DelegateEscapeSadPath.cs");

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && (d.Id.StartsWith("CS") || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));
    }
}

