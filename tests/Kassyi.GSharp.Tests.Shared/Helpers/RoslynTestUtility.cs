#nullable enable
#pragma warning disable RS0030 // Do not use banned APIs
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

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

    /// <summary>Automatically resolves the appropriate .cu.cs kernel file for a given test class based on naming conventions.</summary>
    public static string ResolveKernelFilePath([CallerFilePath] string callerFilePath = "")
    {
        var directory = Path.GetDirectoryName(callerFilePath) ?? string.Empty;
        var testClassName = Path.GetFileNameWithoutExtension(callerFilePath);

        string baseName = testClassName.EndsWith("Tests")
            ? testClassName.Substring(0, testClassName.Length - 5)
            : testClassName;

        var candidates = new[]
        {
            Path.Combine(directory, $"{baseName}TestKernels.cu.cs"),
            Path.Combine(directory, $"{baseName}Kernels.cu.cs"),
            Path.Combine(directory, $"{testClassName}Kernels.cu.cs"),
            Path.Combine(directory, $"{testClassName}.cu.cs")
        };

        var targetFile = candidates.FirstOrDefault(File.Exists);
        if (targetFile == null)
        {
            throw new FileNotFoundException(
                $"Could not automatically resolve kernel file for test {testClassName}. Tried: {string.Join(", ", candidates.Select(Path.GetFileName))}");
        }

        return targetFile;
    }

    /// <summary>Retrieves the compilation context (Compilation, SyntaxTree, SemanticModel) for a dynamically resolved test kernel file.</summary>
    public static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetKernelContext(
        [CallerFilePath] string callerFilePath = "")
    {
        var filePath = ResolveKernelFilePath(callerFilePath);
        var compilation = MegaCompilationCache.GetDynamicCompilation(filePath);
        var tree = compilation.SyntaxTrees.First(t => t.FilePath == filePath);
        var model = compilation.GetSemanticModel(tree);
        return (compilation, tree, model);
    }

    /// <summary>Retrieves the compilation context for a specific test case file relative to the TestCases directory.</summary>
    public static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetTestCaseContext(
        string relativePathToTestCase, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        var filePath = Path.Combine(testCasesDir, relativePathToTestCase);
        var compilation = MegaCompilationCache.GetDynamicCompilation(filePath);
        var tree = compilation.SyntaxTrees.First(t => t.FilePath == filePath);
        var model = compilation.GetSemanticModel(tree);
        return (compilation, tree, model);
    }

    /// <summary>Gets the base compilation references to be used for dynamically compiled tests or analyzers.</summary>
    public static IEnumerable<MetadataReference> BaseReferences => MegaCompilationCache.BaseCompilation.References;

    /// <summary>Retrieves the semantic model and method symbol for a given method in a dynamically resolved test kernel file.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext<TTarget>(
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        return MegaCompilationCache.GetMethodContext<TTarget>(methodName, callerFilePath);
    }

    /// <summary>Retrieves a named type symbol from the base mega compilation.</summary>
    public static INamedTypeSymbol GetNamedTypeSymbol(string typeName)
    {
        return MegaCompilationCache.GetNamedTypeSymbol(typeName);
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

    /// <summary>Creates a dummy semantic model and method symbol for testing analyzers or isolated transpiler nodes.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        var src = "class Dummy_RoslynTestUtility { public void M() {} }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Dummy")
            .AddReferences(BaseReferences)
            .AddSyntaxTrees(tree);
        var model = comp.GetSemanticModel(tree);
        var sym = model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First()) as IMethodSymbol;
        return (model, sym!);
    }

    /// <summary>Retrieves the semantic model and method symbol for a method located within the caller's own source file.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetLocalMethodContext(
        string typeName,
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        var source = File.ReadAllText(callerFilePath);
        var comp = CreateCompilation(source);
        var typeSymbol = comp.GetSymbolsWithName(typeName, SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
        var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().First();
        var tree = methodSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        return (comp.GetSemanticModel(tree), methodSymbol);
    }

    /// <summary>Transpiles a method declared in the mega compilation by type and method name into CUDA C++ source.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(
        string typeName,
        string methodName,
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        var (semanticModel, methodSymbol) = MegaCompilationCache.GetMethodContext(typeName, methodName);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<Diagnostic>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice,
                new GSharpCudaDialect(), out var _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method from dynamic compilation inferred from the caller file into CUDA C++ source.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        // Try to resolve the kernel file, but don't fail if it doesn't exist yet (might be testing local .cs file)
        string? kernelFile = null;
        try { kernelFile = callerFilePath.EndsWith(".cu.cs") ? callerFilePath : ResolveKernelFilePath(callerFilePath); } catch { }

        var paths = kernelFile != null ? new[] { callerFilePath, kernelFile } : new[] { callerFilePath };
        
        var (semanticModel, methodSymbol) =
            MegaCompilationCache.GetMethodContext(targetType, methodName, paths);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<Diagnostic>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, d => diagnostics.Add(d), isDevice,
                new GSharpCudaDialect(), out var _);
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
    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source,
        DiagnosticAnalyzer analyzer, string targetDialect = "Cuda")
    {
        var compilation = CreateCompilation(source);
        var analyzerOptions =
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(targetDialect));
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), analyzerOptions);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>Executes the GpuKernelGenerator on the given source text and produces the output compilation and diagnostics.</summary>
    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)
        RunGenerator(string source, string targetDialect = "Cuda", Dictionary<string, string>? additionalOptions = null)
    {
        var compilation = CreateCompilation(source);

        var generator = new Kassyi.GSharp.Compiler.GpuKernelGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp11),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(targetDialect, additionalOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (driver, outputCompilation, diagnostics);
    }

    /// <summary>Initiates a fluent test assertion builder for Source Generator verification on a test case file.</summary>
    public static GeneratorTestConfig TestGenerator(string relativePathToTestCases,
        [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new GeneratorTestConfig(Path.Combine(testCasesDir, relativePathToTestCases));
    }

    /// <summary>Initiates a fluent test assertion builder for Diagnostic Analyzer verification on a test case file.</summary>
    public static AnalyzerTestConfig TestAnalyzer(string relativePathToTestCases, DiagnosticAnalyzer analyzer,
        [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        return new AnalyzerTestConfig(Path.Combine(testCasesDir, relativePathToTestCases), analyzer);
    }
}

/// <summary>Fluent assertion helper for Source Generator testing against source files.</summary>
public class GeneratorTestConfig
{
    private readonly string _fullPath;
    public string TargetDialect { get; private set; } = "Cuda";

    public GeneratorTestConfig(string fullPath)
    {
        _fullPath = fullPath;
    }

    public GeneratorTestConfig WithDialect(string dialect)
    {
        TargetDialect = dialect;
        return this;
    }

    /// <summary>Asserts that running the generator reports all specified diagnostic error IDs.</summary>
    public async Task ReportsErrorsAsync(params string[] expectedDiagnosticIds)
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source, TargetDialect);
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
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source, TargetDialect);
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
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, TargetDialect);
        return driver;
    }

    /// <summary>Runs the generator and retrieves all combined compilation and generator diagnostics.</summary>
    public async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync()
    {
        var source = await File.ReadAllTextAsync(_fullPath);
        var (_, outputCompilation, diagnostics) = RoslynTestUtility.RunGenerator(source, TargetDialect);
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
    public string TargetDialect { get; private set; } = "Cuda";

    public AnalyzerTestConfig(string fullPath, DiagnosticAnalyzer analyzer)
    {
        _fullPath = fullPath;
        _analyzer = analyzer;
    }

    public AnalyzerTestConfig WithDialect(string dialect)
    {
        TargetDialect = dialect;
        return this;
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
        return await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, _analyzer, TargetDialect);
    }

    /// <summary>Asserts that running the analyzer reports zero diagnostics.</summary>
    public async Task ReportsNoDiagnosticsAsync()
    {
        var diagnostics = await GetDiagnosticsAsync();
        Assert.Empty(diagnostics);
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
    public override AnalyzerConfigOptions GlobalOptions { get; }

    public TestAnalyzerConfigOptionsProvider(string targetDialect = "Cuda",
        Dictionary<string, string>? additionalOptions = null)
    {
        GlobalOptions = new TestAnalyzerConfigOptions(targetDialect, additionalOptions);
    }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly string _targetDialect;
    private readonly Dictionary<string, string>? _additionalOptions;

    public TestAnalyzerConfigOptions(string targetDialect, Dictionary<string, string>? additionalOptions)
    {
        _targetDialect = targetDialect;
        _additionalOptions = additionalOptions;
    }

#pragma warning disable CS8765
    public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
    {
        if (key == "build_property.GSharpTargetDialect")
        {
            value = _targetDialect;
            return true;
        }

        if (_additionalOptions != null && _additionalOptions.TryGetValue(key, out value))
        {
            return true;
        }

        value = null;
        return false;
    }
}
#nullable restore

