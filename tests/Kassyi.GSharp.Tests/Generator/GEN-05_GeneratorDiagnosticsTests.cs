using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using VerifyXunit;

namespace Kassyi.GSharp.Tests;

public class GEN_05_GeneratorDiagnosticsTests
{
    private static (GeneratorDriver Driver, Compilation Compilation, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics) RunIsolatedGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.RemoveAllSyntaxTrees().AddSyntaxTrees(syntaxTree);

        var generator = new Kassyi.GSharp.Compiler.GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp11));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    public async Task TestSadPathDiagnostics()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Generator", "GEN-05_SadPathDiagnostics.cu.cs");
        var source = await File.ReadAllTextAsync(path);
        var (driver, _, _) = RunIsolatedGenerator(source);

        await Verifier.Verify(driver).UseMethodName("TestSadPathDiagnostics");
    }

    [Fact]
    [Trait("FeatureId", "GEN-06")]
    public async Task TestComprehensiveSpecKernelGeneratedSource()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "GEN-06_ComprehensiveSpecKernel.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = RunIsolatedGenerator(source);

        await Verifier.Verify(driver).UseMethodName("TestComprehensiveSpecKernelGeneratedSource");
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    [Trait("FeatureId", "GEN-07")]
    public async Task Test_UnsupportedFeatures_ReportsDiagnostics()
    {
        var path = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Generator", "GEN-07_UnsupportedFeatures.cu.cs");
        var source = await File.ReadAllTextAsync(path);

        var (driver, _, _) = RunIsolatedGenerator(source);

        await Verifier.Verify(driver).UseMethodName("Test_UnsupportedFeatures_ReportsDiagnostics");
    }
}
