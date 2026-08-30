using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_03_CollectionsAndGenericsSadPathTests
{
    private static string GetTestCasePath(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        return Path.Combine(testCasesDir, "Analyzers", fileName);
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM03_InterfaceBoxing_SadPath()
    {
        var path = GetTestCasePath("MEM-03_InterfaceBoxingSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(allDiagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id.StartsWith("CS")));
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM03_UndisposedCollectionField_SadPath()
    {
        var path = GetTestCasePath("MEM-03_UndisposedCollectionFieldSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.True(allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning));
        Assert.True(allDiagnostics.Any(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id || d.Id == GSharpDiagnostics.ObjectCreationNotSupported.Id || d.Id.StartsWith("CS")));
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM03_CollectionSpreadDuplication_SadPath()
    {
        var path = GetTestCasePath("MEM-03_CollectionSpreadDuplicationSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(compilation.GetDiagnostics());

        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }

    [Fact]
    [Trait("FeatureId", "MEM-03")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM03_UnconsumedCustomEnumerator_SadPath()
    {
        var path = GetTestCasePath("MEM-03_UnconsumedCustomEnumeratorSadPath.cs");
        var source = await File.ReadAllTextAsync(path);

        var analyzer = new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer();
        var diagnostics = await Kassyi.GSharp.Tests.Helpers.RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, analyzer);

        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics.Any(d => d.Id == Kassyi.GSharp.Compiler.Diagnostics.GSharpDiagnostics.ManualEnumeratorNotSupported.Id), "Did not find GSH0013. Found:\n" + string.Join("\n", diagnostics.Select(d => d.Id + ": " + d.GetMessage())));
    }
}
