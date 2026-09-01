using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;
using System.Linq;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_17_ReservedIdentifierSadPathTests
{
    [Fact]
    [Trait("Category", "Analyzer")]
    [Trait("FeatureId", "SYN-17")]
    public void Test_SYN17_ReservedIdentifier_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("SYN-17_ReservedIdentifierSadPath.cs");

        var gsh0017Diagnostics =
            diagnostics.Where(d => d.Id == GSharpDiagnostics.ReservedIdentifierNotAllowed.Id).ToList();

        // Expecting 4 errors for __invalid_var, hoge__boo, __param, and __InvalidMethod
        Assert.Equal(4, gsh0017Diagnostics.Count);
        Assert.All(gsh0017Diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
