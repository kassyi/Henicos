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

    /// <summary>Attempts to resolve the companion .cu.cs kernel file for a given test file without throwing when absent.</summary>
    public static bool TryResolveKernelFilePath(string callerFilePath, out string? kernelFilePath)
    {
        if (callerFilePath.EndsWith(".cu.cs", StringComparison.OrdinalIgnoreCase) && File.Exists(callerFilePath))
        {
            kernelFilePath = callerFilePath;
            return true;
        }

        var directory = Path.GetDirectoryName(callerFilePath) ?? string.Empty;
        var testClassName = Path.GetFileNameWithoutExtension(callerFilePath);

        var baseName = testClassName.EndsWith("Tests", StringComparison.Ordinal)
            ? testClassName.Substring(0, testClassName.Length - 5)
            : testClassName;

        string[] candidates =
        [
            Path.Combine(directory, $"{baseName}TestKernels.cu.cs"),
            Path.Combine(directory, $"{baseName}Kernels.cu.cs"),
            Path.Combine(directory, $"{testClassName}Kernels.cu.cs"),
            Path.Combine(directory, $"{testClassName}.cu.cs")
        ];

        kernelFilePath = candidates.FirstOrDefault(File.Exists);
        return kernelFilePath != null;
    }

    /// <summary>Automatically resolves the appropriate .cu.cs kernel file for a given test class based on naming conventions.</summary>
    public static string ResolveKernelFilePath([CallerFilePath] string callerFilePath = "")
    {
        if (TryResolveKernelFilePath(callerFilePath, out var targetFile) && targetFile != null)
        {
            return targetFile;
        }

        var testClassName = Path.GetFileNameWithoutExtension(callerFilePath);
        var baseName = testClassName.EndsWith("Tests", StringComparison.Ordinal)
            ? testClassName.Substring(0, testClassName.Length - 5)
            : testClassName;

        throw new FileNotFoundException(
            $"Could not automatically resolve kernel file for test {testClassName}. Tried: {baseName}TestKernels.cu.cs, {baseName}Kernels.cu.cs, {testClassName}Kernels.cu.cs, {testClassName}.cu.cs");
    }

    /// <summary>Retrieves the compilation context (Compilation, SyntaxTree, SemanticModel) for a dynamically resolved test kernel file.</summary>
    public static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetKernelContext(
        [CallerFilePath] string callerFilePath = "")
    {
        var filePath = ResolveKernelFilePath(callerFilePath);
        var (compilation, tree) = MegaCompilationCache.GetSourceContext(filePath);
        var model = compilation.GetSemanticModel(tree);
        return (compilation, tree, model);
    }

    /// <summary>Retrieves the compilation context for a specific test case file relative to the TestCases directory.</summary>
    public static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetTestCaseContext(
        string relativePathToTestCase, [CallerFilePath] string callerFilePath = "")
    {
        var testCasesDir = GetTestCasesDirectory(callerFilePath);
        var filePath = Path.Combine(testCasesDir, relativePathToTestCase);
        var (compilation, tree) = MegaCompilationCache.GetSourceContext(filePath);
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
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
#pragma warning disable RS0030
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
#pragma warning restore RS0030

        return MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
    }

    /// <summary>Creates a dummy semantic model and method symbol for testing analyzers or isolated transpiler nodes.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        const string Src = "class Dummy_RoslynTestUtility { public void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(Src);
        var comp = CSharpCompilation.Create("Dummy")
            .AddReferences(BaseReferences)
            .AddSyntaxTrees(tree);
        var model = comp.GetSemanticModel(tree);
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var sym = model.GetDeclaredSymbol(methodDecl)
            ?? throw new InvalidOperationException("Failed to resolve declared symbol for Dummy_RoslynTestUtility.M.");
        return (model, sym);
    }

    /// <summary>Retrieves the semantic model and method symbol for a method located within the caller's own source file.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetLocalMethodContext(
        string typeName,
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        var (comp, tree) = MegaCompilationCache.GetSourceContext(callerFilePath);
        var model = comp.GetSemanticModel(tree);
        var typeSymbol = tree.GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(t => model.GetDeclaredSymbol(t))
            .OfType<INamedTypeSymbol>()
            .First(t => t.Name == typeName);
        var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().First();
        return (model, methodSymbol);
    }

    /// <summary>Transpiles a method declared in the mega compilation by type and method name into target dialect C++ source.</summary>
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
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method from dynamic compilation inferred from the caller file into target dialect C++ source.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        var pathList = new List<string> { callerFilePath };
        if (TryResolveKernelFilePath(callerFilePath, out var kernelFile) && kernelFile != null)
        {
            pathList.Add(kernelFile);
        }

        var dir = Path.GetDirectoryName(callerFilePath);
        if (dir != null)
        {
            var targetTypeKernelFile = Path.Combine(dir, $"{targetType.Name}.cu.cs");
            if (File.Exists(targetTypeKernelFile) && !pathList.Contains(targetTypeKernelFile))
            {
                pathList.Add(targetTypeKernelFile);
            }
        }

        var (semanticModel, methodSymbol) =
            MegaCompilationCache.GetMethodContext(targetType, methodName, pathList.ToArray());
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<Diagnostic>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method declared on <typeparamref name="TTarget"/> into target dialect C++ source with type safety.</summary>
    public static (string CudaSource, List<Diagnostic> Diagnostics) TranspileMethod<TTarget>(
        string methodName,
        [CallerFilePath] string callerFilePath = "",
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
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
        var generator = new GpuKernelGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
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
