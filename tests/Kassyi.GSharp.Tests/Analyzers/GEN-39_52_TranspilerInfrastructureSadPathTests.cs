using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_39_52_TranspilerInfrastructureSadPathTests
{
    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GEN-39")]
    public void Test_GEN39_InvalidBclIntrinsicMapping_DoesNotCrash()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("GEN-39_52_InvalidMappingSadPath.cs");
        // No specific assertion, just that we can get diagnostics without crashing
        Assert.NotNull(diagnostics);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GEN-52")]
    public void Test_GEN52_UnknownAttributesAndInvalidIntrinsics_ReportsDiagnostics()
    {
        var diagnostics = MegaCompilationCache.GetGeneratorDiagnosticsForFile("GEN-39_52_InvalidMappingSadPath.cs");
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}

