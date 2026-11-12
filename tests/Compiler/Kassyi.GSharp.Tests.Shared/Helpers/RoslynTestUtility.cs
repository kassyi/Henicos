#nullable enable
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Provides utility methods and fluent helpers for Roslyn compilation, generator, analyzer, and transpiler testing.
/// </summary>
/// <remarks>
/// The one place that parses test sources: every compilation a test uses starts from <see cref="BaseCompilation"/>
/// and the parse options of the test suite, and the compilations of the files of the test assembly are parsed once per
/// process.
/// </remarks>
public static class RoslynTestUtility
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly Lazy<CSharpCompilation> s_compilerSources = new(CreateCompilerSourceCompilation);

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

    /// <summary>Reads the source of a test case file, given relative to the TestCases directory.</summary>
    public static string ReadTestCase(string relativePathToTestCases) =>
        File.ReadAllText(RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", relativePathToTestCases));

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

        var baseName = WithoutTestsSuffix(testClassName);

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

    // FooTests -> Foo
    private static ReadOnlySpan<char> WithoutTestsSuffix(string testClassName)
    {
        const string Suffix = "Tests";
        var name = testClassName.AsSpan();
        return name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
    }

    /// <summary>Automatically resolves the appropriate .cu.cs kernel file for a given test class based on naming conventions.</summary>
    public static string ResolveKernelFilePath([CallerFilePath] string callerFilePath = "")
    {
        if (TryResolveKernelFilePath(callerFilePath, out var targetFile) && targetFile != null)
        {
            return targetFile;
        }

        var testClassName = Path.GetFileNameWithoutExtension(callerFilePath);
        var baseName = WithoutTestsSuffix(testClassName);

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

    /// <summary>Retrieves the shared compilation and the syntax tree of the calling test file.</summary>
    public static (Compilation Compilation, SyntaxTree Tree) GetCallerSourceContext(
        [CallerFilePath] string callerFilePath = "") =>
        MegaCompilationCache.GetSourceContext(callerFilePath);

    /// <summary>
    /// Returns the compilation and syntax tree for <paramref name="path"/>: the shared compilation for files built into
    /// the test assembly, or a cached per-file compilation for isolated test-case inputs.
    /// </summary>
    internal static (CSharpCompilation Compilation, SyntaxTree Tree) GetSourceContext(string path) =>
        MegaCompilationCache.GetSourceContext(path);

    /// <summary>The number of isolated compilations the cache keeps alive by itself.</summary>
    internal const int IsolatedCapacity = MegaCompilationCache.IsolatedCapacity;

    /// <summary>Gets the number of isolated compilations the cache keeps alive by itself.</summary>
    internal static int IsolatedRecentCount => MegaCompilationCache.IsolatedRecentCount;

    /// <summary>
    /// Gets the reference-only compilation (no syntax trees) that every test compilation starts from.
    /// </summary>
    public static CSharpCompilation BaseCompilation => MegaCompilationCache.BaseCompilation;

    /// <summary>Gets the base compilation references to be used for dynamically compiled tests or analyzers.</summary>
    public static IEnumerable<MetadataReference> BaseReferences => BaseCompilation.References;

    /// <summary>Gets the compilation of every source file built into the test assembly, for tests that scan all of them.</summary>
    public static Compilation SharedCompilation => MegaCompilationCache.Compilation;

    /// <summary>
    /// Gets a compilation of every source file of the compiler project (outside its <c>obj</c> and <c>bin</c>
    /// directories), each tree with its full path, parsed once per process, for the architecture tests that scan the
    /// compiler.
    /// </summary>
    public static CSharpCompilation CompilerSourceCompilation => s_compilerSources.Value;

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

    /// <summary>Creates a Roslyn compilation by parsing each source text and adding it to the mega compilation cache.</summary>
    public static CSharpCompilation CreateCompilation(params string[] sources) =>
        CreateCompilationAt(s_parseOptions, sources.Select(static source => (string.Empty, source)).ToArray());

    /// <summary>
    /// Creates a Roslyn compilation of one source text whose tree has the file path <paramref name="path"/>, so that
    /// places in the generated code (error sites, messages) name it.
    /// </summary>
    public static CSharpCompilation CreateCompilationAt(string path, string source) => CreateCompilationAt((path, source));

    /// <summary>
    /// Creates a Roslyn compilation of one source text whose tree has the file path <paramref name="path"/>, parsed
    /// with <paramref name="preprocessorSymbols"/> defined, as a project defines them for every file.
    /// </summary>
    public static CSharpCompilation CreateCompilationAt(
        string path,
        string source,
        IEnumerable<string> preprocessorSymbols) =>
        CreateCompilationAt(s_parseOptions.WithPreprocessorSymbols(preprocessorSymbols), [(path, source)]);

    /// <summary>
    /// Creates a Roslyn compilation of several source texts, in that order, each tree with its file path, so that the
    /// declaration order of a partial type and the places in the generated code follow the paths.
    /// </summary>
    public static CSharpCompilation CreateCompilationAt(params (string Path, string Source)[] files) =>
        CreateCompilationAt(s_parseOptions, files);

    private static CSharpCompilation CreateCompilationAt(
        CSharpParseOptions options,
        (string Path, string Source)[] files)
    {
#pragma warning disable RS0030 // The single place that parses test sources; tests reach it through RoslynTestUtility
        var syntaxTrees = files.Select(file => CSharpSyntaxTree.ParseText(file.Source, options, file.Path)).ToArray();
#pragma warning restore RS0030

        return BaseCompilation.AddSyntaxTrees(syntaxTrees);
    }

    private static CSharpCompilation CreateCompilerSourceCompilation()
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        var files = Directory.EnumerateFiles(compilerRoot, "*.cs")
            .Concat(Directory.EnumerateDirectories(compilerRoot)
                .Where(static directory => Path.GetFileName(directory) is not ("obj" or "bin"))
                .SelectMany(static directory =>
                    Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            ).Order(StringComparer.Ordinal)
            .Select(static path => (path, File.ReadAllText(path)))
            .ToArray();
        return CreateCompilationAt(files);
    }

    /// <summary>Creates a dummy semantic model and method symbol for testing analyzers or isolated transpiler nodes.</summary>
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        const string Src = "class Dummy_RoslynTestUtility { public void M() {} }";
        var comp = CreateCompilation(Src);
        var tree = comp.SyntaxTrees.Single();
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
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod(
        string typeName,
        string methodName,
        bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        var (semanticModel, methodSymbol) = MegaCompilationCache.GetMethodContext(typeName, methodName);
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var diagnostics = new List<TranspileFailure>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method from dynamic compilation inferred from the caller file into target dialect C++ source.</summary>
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod(
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

        var diagnostics = new List<TranspileFailure>();
        dialect ??= new GSharpCudaDialect();
        var cudaSource =
            MethodTranspiler.GenerateCudaSource(methodDecl, semanticModel, diagnostics.Add, isDevice,
                dialect, out _);
        return (cudaSource, diagnostics);
    }

    /// <summary>Transpiles a method declared on <typeparamref name="TTarget"/> into target dialect C++ source with type safety.</summary>
    public static (string CudaSource, List<TranspileFailure> Failures) TranspileMethod<TTarget>(
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
        RunGenerator(string source, string targetDialect = "Cuda", Dictionary<string, string>? additionalOptions = null) =>
        RunGenerator(CreateCompilation(source), targetDialect, additionalOptions);

    /// <summary>Executes the GpuKernelGenerator on the given compilation and produces the output compilation and diagnostics.</summary>
    public static (GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics)
        RunGenerator(Compilation compilation, string targetDialect = "Cuda", Dictionary<string, string>? additionalOptions = null)
    {
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

    /// <summary>
    /// Process-wide Roslyn compilations for transpiler tests, parsed once up front instead of growing per test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Shared</b> sources are the files the test assembly itself compiles (see <c>Kassyi.GSharp.Tests.Shared.projitems</c>).
    /// They are known to compile together, so they are parsed in one batch into a single immutable <see cref="Compilation"/>
    /// whose binding caches are reused by every test.
    /// </para>
    /// <para>
    /// <b>Isolated</b> sources are test-case inputs excluded from the build (<c>TestCases/**</c> and the embedded
    /// <c>Transpiler/*Kernels.cu.cs</c> resources). They contain deliberately invalid code and duplicate type names, so each
    /// one gets its own compilation built from <see cref="BaseCompilation"/>. The cache keeps the
    /// <see cref="IsolatedCapacity"/> most recently used ones and, beyond them, only those a caller still holds.
    /// </para>
    /// </remarks>
    private static class MegaCompilationCache
    {
        private static readonly Lazy<CSharpCompilation> s_baseCompilation = new(() =>
        {
            var baseDir = AppContext.BaseDirectory;
            var trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").AsSpan();
            var references = new List<MetadataReference>();
            foreach (var range in trustedAssemblies.Split(Path.PathSeparator))
            {
                var path = trustedAssemblies[range];
                if (!path.IsWhiteSpace())
                {
                    // CreateFromFile copies the image into native memory and closes the file, so the test host neither
                    // locks the assemblies a later build overwrites (MSB3026) nor keeps tens of MB of images on the large
                    // object heap.
                    references.Add(MetadataReference.CreateFromFile(path.ToString()));
                }
            }

            // Ensure Core assembly is referenced (it might not be in trustedAssemblies if the test doesn't explicitly reference it)
            var coreDllPath = typeof(Kassyi.GSharp.Core.Gpu).Assembly.Location;
            AddIfMissing(references, coreDllPath);

            // Implicitly reference Math library if present in the same directory, to support standard library tests
            var mathDllPath =
                Path.Combine(Path.GetDirectoryName(coreDllPath) ?? baseDir, "Kassyi.GSharp.Stdlib.GpuMath.dll");
            AddIfMissing(references, mathDllPath);

            // Implicitly reference ZeroAlloc.Linq library if present
            var linqDllPath = Path.Combine(Path.GetDirectoryName(coreDllPath) ?? baseDir, "Kassyi.ZeroAlloc.Linq.dll");
            AddIfMissing(references, linqDllPath);

            var baseComp = CSharpCompilation.Create("MegaTestCompilationBase")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
                .AddReferences(references);

            return baseComp;
        });

        private static void AddIfMissing(List<MetadataReference> references, string path)
        {
            if (File.Exists(path) &&
                !references.Any(r => string.Equals(r.Display, path, StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

#pragma warning disable RS0030 // The single place that parses test sources; tests reach it through RoslynTestUtility
        private static readonly SyntaxTree s_globalUsingsTree = CSharpSyntaxTree.ParseText(
            "global using System;" +
            "global using System.Collections.Generic;" +
            "global using System.IO;" +
            "global using System.Linq;" +
            "global using System.Runtime.CompilerServices;" +
            "global using System.Runtime.InteropServices;" +
            "global using Kassyi.GSharp.Core;",
            s_parseOptions,
            path: "__GlobalUsings.g.cs");
#pragma warning restore RS0030

        private static readonly string s_sharedRoot = ResolveSharedRoot();

        // Mirrors the Compile Exclude list in Kassyi.GSharp.Tests.Shared.projitems. Keep both in sync;
        // MegaCompilationCacheTests guards against drift by asserting the shared compilation has no duplicate definitions.
        private static readonly string[] s_isolatedDirectories = ["TestCases", "obj", "bin", Path.Combine("Generator", "Snapshots")];
        private static readonly string[] s_isolatedFiles =
        [
            Path.Combine("Transpiler", "GpuMacroTranspilerRuleKernels.cu.cs"),
            Path.Combine("Transpiler", "MacroTranspilerKernels.cu.cs"),
            Path.Combine("Transpiler", "MemoryExtensionsTranspilerRuleKernels.cu.cs")
        ];

        private static readonly Lazy<(CSharpCompilation Compilation, Dictionary<string, SyntaxTree> Trees)> s_shared =
            new(CreateSharedCompilation, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>The number of isolated compilations the cache keeps alive by itself.</summary>
        /// <remarks>
        /// Each one binds its test case against every reference, so keeping all of them (several hundred in a run) held
        /// hundreds of MB for the whole run. The test classes that run at the same time use far fewer files than this.
        /// </remarks>
        public const int IsolatedCapacity = 32;

        private static readonly RecentlyUsedWeakCache<string, CSharpCompilation> s_isolated =
            new(IsolatedCapacity, StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the number of isolated compilations the cache keeps alive by itself.</summary>
        public static int IsolatedRecentCount => s_isolated.RecentCount;

        /// <summary>Gets the reference-only compilation (no syntax trees) used as the base of every compilation.</summary>
        public static CSharpCompilation BaseCompilation => s_baseCompilation.Value;

        /// <summary>Gets the single compilation holding every shared source file, parsed once per process.</summary>
        public static CSharpCompilation Compilation => s_shared.Value.Compilation;

        /// <summary>
        /// Returns the compilation and syntax tree for <paramref name="path"/>: the shared compilation for files built into the
        /// test assembly, or a cached per-file compilation for isolated test-case inputs.
        /// </summary>
        public static (CSharpCompilation Compilation, SyntaxTree Tree) GetSourceContext(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var shared = s_shared.Value;
            if (shared.Trees.TryGetValue(fullPath, out var tree))
            {
                return (shared.Compilation, tree);
            }

            if (!IsIsolated(fullPath))
            {
                throw new InvalidOperationException(
                    $"{fullPath} is neither a shared source under {s_sharedRoot} nor an isolated test-case input.");
            }

            // The compilation holds the global usings tree and the test case, in that order (CreateIsolatedCompilation)
            var compilation = s_isolated.GetOrAdd(fullPath, CreateIsolatedCompilation);
            return (compilation, compilation.SyntaxTrees[1]);
        }

        public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext(
            Type targetType,
            string methodName,
            params string[] sourceFilePaths)
        {
            foreach (var path in sourceFilePaths)
            {
                if (!File.Exists(path)) continue;

                var (compilation, tree) = GetSourceContext(path);
                var model = compilation.GetSemanticModel(tree);
                var typeSymbol = tree.GetRoot().DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(s => model.GetDeclaredSymbol(s))
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault(s => s.Name == targetType.Name);

                if (typeSymbol == null) continue;

                var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>()
                                       .FirstOrDefault(m => m.DeclaringSyntaxReferences.Length > 0)
                                   ?? throw new InvalidOperationException(
                                       $"Method {methodName} with source code not found in {typeSymbol.Name}.");

                // A partial type may declare the method in another file of the same compilation.
                var methodTree = methodSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
                return (methodTree == tree ? model : compilation.GetSemanticModel(methodTree), methodSymbol);
            }

            throw new InvalidOperationException(
                $"Type {targetType.Name} with source code not found in {string.Join(", ", sourceFilePaths)}.");
        }

        public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext<TTarget>(
            string methodName,
            string callerFilePath)
        {
            if (callerFilePath.EndsWith(".cu.cs", StringComparison.OrdinalIgnoreCase))
            {
                return GetMethodContext(typeof(TTarget), methodName, callerFilePath);
            }

            if (TryResolveKernelFilePath(callerFilePath, out var targetFile) && targetFile != null)
            {
                return GetMethodContext(typeof(TTarget), methodName, callerFilePath, targetFile);
            }

            return GetMethodContext(typeof(TTarget), methodName, callerFilePath);
        }

        public static INamedTypeSymbol GetNamedTypeSymbol(string typeName)
        {
            var symbol = Compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault();

            return symbol ?? throw new InvalidOperationException($"Type {typeName} not found in the mega compilation.");
        }

        public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext(string typeName, string methodName)
        {
            var typeSymbol = GetNamedTypeSymbol(typeName);

            var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault()
                               ?? throw new InvalidOperationException(
                                   $"Method {methodName} not found in {typeSymbol.Name}.");

            var syntaxTree = methodSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
            var model = Compilation.GetSemanticModel(syntaxTree);
            return (model, methodSymbol);
        }

        private static (CSharpCompilation, Dictionary<string, SyntaxTree>) CreateSharedCompilation()
        {
            var paths = Directory.EnumerateFiles(s_sharedRoot, "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(p => !IsIsolated(p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            // Parse in parallel into a pre-sized array so tree order stays deterministic.
            var trees = new SyntaxTree[paths.Length];
            Parallel.For(0, paths.Length, i => trees[i] = ParseFile(paths[i]));

            var byPath = new Dictionary<string, SyntaxTree>(paths.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paths.Length; i++)
            {
                byPath.Add(paths[i], trees[i]);
            }

            var compilation = BaseCompilation.AddSyntaxTrees(trees.Prepend(s_globalUsingsTree));
            return (compilation, byPath);
        }

        private static CSharpCompilation CreateIsolatedCompilation(string path) =>
            BaseCompilation.AddSyntaxTrees(s_globalUsingsTree, ParseFile(path));

        private static SyntaxTree ParseFile(string path)
        {
#pragma warning disable RS0030 // The single place that parses test sources; tests reach it through RoslynTestUtility
            return CSharpSyntaxTree.ParseText(File.ReadAllText(path), s_parseOptions, path: path);
#pragma warning restore RS0030
        }

        private static bool IsIsolated(string fullPath)
        {
            if (fullPath.EndsWith(".verified.cs", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".received.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var relative = Path.GetRelativePath(s_sharedRoot, fullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                // Outside the shared project (e.g. a test-case file of another test project).
                return true;
            }

            foreach (var directory in s_isolatedDirectories)
            {
                if (relative.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var file in s_isolatedFiles)
            {
                if (string.Equals(relative, file, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveSharedRoot([CallerFilePath] string thisFilePath = "")
        {
            var helpersDir = Path.GetDirectoryName(thisFilePath)
                             ?? throw new InvalidOperationException("Could not resolve the Helpers directory.");
            return Path.GetFullPath(Path.Combine(helpersDir, ".."));
        }
    }
}
