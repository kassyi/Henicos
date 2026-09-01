using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Helpers;

/// <summary>Provides utility methods and fluent helpers for Roslyn compilation, generator, analyzer, and transpiler testing.</summary>
public static class RoslynTestUtility
{
    /// <summary>Resolves the absolute path to the TestCases directory relative to the caller test file.</summary>
    public static string GetTestCasesDirectory([CallerFilePath] string callerFilePath = "")
    {
        var fileDir = Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(fileDir))
        {
            throw new DirectoryNotFoundException("Could not determine test project directory.");
        }
        var projectDir = Path.GetFullPath(Path.Combine(fileDir, ".."));
        var testCasesDir = Path.Combine(projectDir, "TestCases");
        if (!Directory.Exists(testCasesDir))
        {
            throw new DirectoryNotFoundException($"TestCases directory not found at: {testCasesDir}");
        }
        return testCasesDir;
    }

    /// <summary>Creates a Roslyn compilation by parsing source text and adding it to the mega compilation cache.</summary>
    public static Compilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
#pragma warning disable RS0030
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
#pragma warning restore RS0030

        return MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
    }

    /// <summary>Transpiles a method declared in the mega compilation by type and method name into CUDA C++ source.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(
        string typeName,
        string methodName,
        bool isDevice = true,
        Kassyi.GSharp.Compiler.Transpiler.Dialects.ITranspilerDialect? dialect = null)
    {
        var (semanticModel, methodSymbol) = MegaCompilationCache.GetMethodContext(typeName, methodName);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<Diagnostic>();
        dialect ??= new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: isDevice);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method from dynamic compilation inferred from the caller file into CUDA C++ source.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        Kassyi.GSharp.Compiler.Transpiler.Dialects.ITranspilerDialect? dialect = null)
    {
        var (semanticModel, methodSymbol) = MegaCompilationCache.GetMethodContext(targetType, methodName, callerFilePath);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<Diagnostic>();
        dialect ??= new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect();
        var cudaSource = MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice: isDevice);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method declared on <typeparamref name="TTarget"/> into CUDA C++ source with type safety.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod<TTarget>(
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        Kassyi.GSharp.Compiler.Transpiler.Dialects.ITranspilerDialect? dialect = null)
    {
        return TranspileMethod(typeof(TTarget), methodName, callerFilePath, isDevice, dialect);
    }

    /// <summary>Runs a DiagnosticAnalyzer on the given source text and returns reported diagnostics.</summary>
    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
        var compilation = CreateCompilation(source);
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new TestAnalyzerConfigOptionsProvider());
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), analyzerOptions);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>Executes the GpuKernelGenerator on the given source text and produces the output compilation and diagnostics.</summary>
    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);

        var generator = new Kassyi.GSharp.Compiler.GpuKernelGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp11),
            optionsProvider: new TestAnalyzerConfigOptionsProvider());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    /// <summary>Initiates a fluent test assertion builder for Source Generator verification on a test case file.</summary>
    public static GeneratorTestConfig TestGenerator(string relativePathToTestCases, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new GeneratorTestConfig(Path.Combine(testCasesDir, relativePathToTestCases));
    }

    /// <summary>Initiates a fluent test assertion builder for Diagnostic Analyzer verification on a test case file.</summary>
    public static AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases, DiagnosticAnalyzer analyzer, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new AnalyzerTestConfig(Path.Combine(testCasesDir, relativePathToTestCases), analyzer);
    }
}

/// <summary>Fluent assertion helper for Source Generator testing against source files.</summary>
public class GeneratorTestConfig
{
    private readonly string _fullPath;

    public GeneratorTestConfig(string fullPath)
    {
        _fullPath = fullPath;
    }

    /// <summary>Asserts that running the generator reports all specified diagnostic error IDs.</summary>
    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(outputCompilation.GetDiagnostics());
        foreach (var id in expectedDiagnosticIds)
        {
            allDiagnostics.AssertContainsError(id);
        }
    }

    /// <summary>Asserts that running the generator reports all specified diagnostic IDs regardless of severity.</summary>
    public async Task ReportsDiagnosticsAsync(params string[] expectedDiagnosticIds)
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        var allDiagnostics = diagnostics.AddRange(outputCompilation.GetDiagnostics());
        foreach (var id in expectedDiagnosticIds)
        {
            allDiagnostics.AssertContains(id);
        }
    }

    /// <summary>Runs the generator and returns the underlying GeneratorDriver.</summary>
    public async Task<GeneratorDriver> GetDriverAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source);
        return driver;
    }

    /// <summary>Runs the generator and retrieves all combined compilation and generator diagnostics.</summary>
    public async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source);
        return diagnostics.AddRange(outputCompilation.GetDiagnostics());
    }

    /// <summary>Asserts that running the generator produces at least one error-level diagnostic.</summary>
    public async Task HasErrorsAsync()
    {
        var allDiagnostics = await GetDiagnosticsAsync();
        Assert.Contains(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Asserts that running the generator produces zero error-level diagnostics.</summary>
    public async Task HasNoErrorsAsync()
    {
        var allDiagnostics = await GetDiagnosticsAsync();
        Assert.DoesNotContain(allDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

/// <summary>Fluent assertion helper for Roslyn Diagnostic Analyzer testing against source files.</summary>
public class AnalyzerTestConfig
{
    private readonly string _fullPath;
    private readonly DiagnosticAnalyzer _analyzer;

    public AnalyzerTestConfig(string fullPath, DiagnosticAnalyzer analyzer)
    {
        _fullPath = fullPath;
        _analyzer = analyzer;
    }

    /// <summary>Asserts that running the analyzer reports all specified diagnostic error IDs.</summary>
    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContainsError(id);
        }
    }

    /// <summary>Asserts that running the analyzer reports all specified diagnostic IDs.</summary>
    public async Task ReportsDiagnosticsAsync(params string[] expectedDiagnosticIds)
    {
        var diagnostics = await GetDiagnosticsAsync();
        foreach (var id in expectedDiagnosticIds)
        {
            diagnostics.AssertContains(id);
        }
    }

    /// <summary>Runs the analyzer and retrieves all reported diagnostics.</summary>
    public async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        return await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, _analyzer);
    }

    /// <summary>Asserts that the analyzer produces at least one error-level diagnostic.</summary>
    public async Task HasErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Asserts that the analyzer produces zero error-level diagnostics.</summary>
    public async Task HasNoErrorsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

#nullable enable
internal class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions();
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string? value)
    {
        if (key == "build_property.GSharpTargetDialect")
        {
#if GSHARP_CPU
            value = "Cpu";
#elif GSHARP_CUDA
            value = "Cuda";
#else
            value = "Cuda"; // Default fallback for tests
#endif
            return true;
        }
        value = null;
        return false;
    }
}
#nullable restore
