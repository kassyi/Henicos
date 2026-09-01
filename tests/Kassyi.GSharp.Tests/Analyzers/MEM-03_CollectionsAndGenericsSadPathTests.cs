using System.Linq;
using Kassyi.GSharp.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_03_CollectionsAndGenericsSadPathTests
{
    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public void Test_MEM03_InterfaceBoxing_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("MEM-03_InterfaceBoxingSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(diagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id.StartsWith("CS")));
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public void Test_MEM03_UndisposedCollectionField_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("MEM-03_UndisposedCollectionFieldSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id.StartsWith("CS")));
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public void Test_MEM03_CollectionSpreadDuplication_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("MEM-03_CollectionSpreadDuplicationSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public void Test_MEM03_UnconsumedCustomEnumerator_SadPath()
    {
        var diagnostics = MegaCompilationCache.GetAllDiagnosticsForFile("MEM-03_UnconsumedCustomEnumeratorSadPath.cs");

        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ForeachNotSupported.Id || d.Id.StartsWith("CS")));
    }
}
