using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_05_UnmanagedBoundariesSadPathTests
{
    private static readonly Lazy<(Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)> s_transpilerResult = new(() =>
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
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
        var files = Directory.GetFiles(Path.Combine(testCasesDir, "Analyzers"), "MEM-05_*.cs");
        foreach (var f in files)
        {
            MegaCompilationCache.RegisterSource(File.ReadAllText(f));
        }
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsForTestAsync(string typeName)
    {
        var typeSymbol = MegaCompilationCache.GetNamedTypeSymbol(typeName);
        var syntaxTree = typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        var source = syntaxTree.ToString();
        var analyzerDiagnostics = await GpuAnalyzerTestRunner.GetAnalyzerDiagnosticsAsync(source, new GpuKernelAnalyzer());
        
        var (compilation, diagnostics) = s_transpilerResult.Value;
        var localDiagnostics = diagnostics.Where(d => d.Location.SourceTree == syntaxTree);
        var localCompDiagnostics = compilation.GetDiagnostics().Where(d => d.Location.SourceTree == syntaxTree);

        return localDiagnostics.Concat(localCompDiagnostics).Concat(analyzerDiagnostics).ToImmutableArray();
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_UnboundedPointer_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_UnboundedPointerTestCase");
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "CS0214" || d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_MallocWarning_ReportsDiagnosticOrWarningAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_MallocWarningTestCase");
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0014");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_StackallocLoop_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_StackallocLoopTestCase");
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0003");
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_Finalizer_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("FinalizerGpuContext");
        Assert.NotEmpty(allDiagnostics);
        Assert.Contains(allDiagnostics, d => d.Id == "GSH0003" && d.GetMessage().Contains("Finalizers"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_DynamicKeyword_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_DynamicKeywordTestCase");
        Assert.NotEmpty(allDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_TypedReference_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_TypedReferenceTestCase");
        Assert.NotEmpty(allDiagnostics);
    }

    [Fact]
    [Trait("FeatureId", "MEM-05")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM05_ComInteropPInvokeGCHandle_ReportsDiagnosticOrErrorAndPreventsCrash()
    {
        var allDiagnostics = await GetDiagnosticsForTestAsync("MEM05_ComInteropPInvokeGCHandleTestCase");
        Assert.NotEmpty(allDiagnostics);
    }
}
