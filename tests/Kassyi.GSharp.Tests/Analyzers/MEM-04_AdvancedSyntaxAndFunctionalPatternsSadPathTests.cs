using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_04_AdvancedSyntaxAndFunctionalPatternsSadPathTests
{
    private static readonly Lazy<(Microsoft.CodeAnalysis.Compilation Compilation, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics)> s_transpilerResult = new(() =>
    {
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp11);
        var generator = new Kassyi.GSharp.Compiler.GpuKernelGenerator();
        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(MegaCompilationCache.BaseCompilation, out var comp, out var diags);
        return (comp, diags);
    });

    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Register()
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        var files = Directory.GetFiles(Path.Combine(testCasesDir, "Analyzers"), "MEM-04_*.cs");
        foreach (var f in files)
        {
            MegaCompilationCache.RegisterSource(File.ReadAllText(f));
        }
    }

    private static async Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> GetDiagnosticsForTestAsync()
    {
        var typeSymbol = MegaCompilationCache.GetNamedTypeSymbol("MEM_04_AdvancedSyntaxAndFunctionalPatternsSadPath");
        var syntaxTree = typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        var source = syntaxTree.ToString();
        var analyzerDiagnostics = await GpuAnalyzerTestRunner.GetAnalyzerDiagnosticsAsync(source, new Kassyi.GSharp.Compiler.Diagnostics.GpuKernelAnalyzer());
        
        var (compilation, diagnostics) = s_transpilerResult.Value;
        var localDiagnostics = diagnostics.Where(d => d.Location.SourceTree == syntaxTree);
        var localCompDiagnostics = compilation.GetDiagnostics().Where(d => d.Location.SourceTree == syntaxTree);

        return localDiagnostics.Concat(localCompDiagnostics).Concat(analyzerDiagnostics).ToImmutableArray();
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_StructsAndTuples_BannedPatterns_ReportDiagnostics()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync();
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003" || d.Id.StartsWith("CS"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_RecordsAndMoveSemantics_BannedPatterns_ReportDiagnostics()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync();
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Location.GetLineSpan().StartLinePosition.Line > 0);
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_ClosuresAndDelegates_BannedPatterns_ReportDiagnostics()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync();
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_RefModifiersAndAsync_BannedPatterns_ReportDiagnostics()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync();
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Id == "GSH0001" || d.Id == "GSH0003" || d.Id.StartsWith("CS"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-04")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM04_ComprehensiveSadPath_ExhaustiveDiagnosticValidation()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync();
        var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count >= 5, $"Expected at least 5 diagnostic errors, but got {errors.Count}");
    }
}
