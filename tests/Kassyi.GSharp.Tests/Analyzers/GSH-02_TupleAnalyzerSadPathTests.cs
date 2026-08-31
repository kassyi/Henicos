using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_02_TupleAnalyzerSadPathTests
{
    [Fact]
    [Trait("FeatureId", "GSH-02")]
    [Trait("Category", "SadPath")]
    public void Test_GSH02_TupleWithReferenceType_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("GSH-02_TupleWithReferenceTypeSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
    }
}
