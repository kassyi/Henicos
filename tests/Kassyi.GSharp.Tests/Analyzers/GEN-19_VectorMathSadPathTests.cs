using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_19_VectorMathSadPathTests
{
    [Fact]
    [Trait("FeatureId", "GEN-19")]
    [Trait("Category", "SadPath")]
    public void Test_GEN19_MismatchedVectorOperators_ReportsRoslynCompilerError()
    {
        var compilerDiagnostics = MegaCompilationCache.GetCompilerDiagnosticsForFile("GEN-19_VectorMathSadPath.cs");
        Assert.Contains(compilerDiagnostics, d => d.Id == "CS0019");
    }

    [Fact]
    [Trait("FeatureId", "GEN-19")]
    [Trait("Category", "SadPath")]
    public void Test_GEN19_UnsupportedTypeInKernel_ReportsGSH0002()
    {
        var generatorDiagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("GEN-19_VectorMathSadPath.cs");
        generatorDiagnostics.AssertContainsError(GSharpDiagnostics.StringNotSupported.Id);
    }
}
