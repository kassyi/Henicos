using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_03_InlineOutVarSadPathTests
{
    [Fact]
    [Trait("FeatureId", "SYN-03")]
    [Trait("Category", "SadPath")]
    public void Test_SYN03_DuplicateOutVar_ReportsDiagnosticAndPreventsCrash()
    {
        var allDiagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-03_DuplicateOutVar.cs");

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && (d.Id == "CS0128" || d.Id == "CS0136"));
    }
}

