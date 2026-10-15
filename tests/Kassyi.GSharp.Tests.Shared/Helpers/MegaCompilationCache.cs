#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

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
/// one gets its own compilation built from <see cref="BaseCompilation"/>.
/// </para>
/// </remarks>
public static class MegaCompilationCache
{
    private static readonly Lazy<CSharpCompilation> s_baseCompilation = new(() =>
    {
        var baseDir = AppContext.BaseDirectory;
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        var references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p =>
            {
                // To avoid MSB3026 (file lock by testhost.exe), load assemblies built by us (in the test output directory) in-memory.
                // System and framework assemblies can be safely loaded from file to massively improve performance.
                if (p.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return (MetadataReference)MetadataReference.CreateFromImage(File.ReadAllBytes(p));
                }

                return (MetadataReference)MetadataReference.CreateFromFile(p);
            })
            .ToList();

        // Ensure Core assembly is referenced (it might not be in trustedAssemblies if the test doesn't explicitly reference it)
        var coreDllPath = typeof(Kassyi.GSharp.Core.Gpu).Assembly.Location;
        if (!references.Any(r => string.Equals(r.Display, coreDllPath, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(coreDllPath)));
        }

        // Implicitly reference Math library if present in the same directory, to support standard library tests
        var mathDllPath =
            Path.Combine(Path.GetDirectoryName(coreDllPath) ?? baseDir, "Kassyi.GSharp.Stdlib.GpuMath.dll");
        if (File.Exists(mathDllPath) &&
            !references.Any(r => string.Equals(r.Display, mathDllPath, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(mathDllPath)));
        }

        // Implicitly reference ZeroAlloc.Linq library if present
        var linqDllPath = Path.Combine(Path.GetDirectoryName(coreDllPath) ?? baseDir, "Kassyi.ZeroAlloc.Linq.dll");
        if (File.Exists(linqDllPath) &&
            !references.Any(r => string.Equals(r.Display, linqDllPath, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(linqDllPath)));
        }

        var baseComp = CSharpCompilation.Create("MegaTestCompilationBase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(references);

        return baseComp;
    });

    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

#pragma warning disable RS0030
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

    private static readonly ConcurrentDictionary<string, Lazy<(CSharpCompilation, SyntaxTree)>> s_isolated =
        new(StringComparer.OrdinalIgnoreCase);

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

        return s_isolated.GetOrAdd(fullPath, static p => new Lazy<(CSharpCompilation, SyntaxTree)>(
            () => CreateIsolatedCompilation(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
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
        [CallerFilePath] string callerFilePath = "")
    {
        if (callerFilePath.EndsWith(".cu.cs", StringComparison.OrdinalIgnoreCase))
        {
            return GetMethodContext(typeof(TTarget), methodName, callerFilePath);
        }

        if (RoslynTestUtility.TryResolveKernelFilePath(callerFilePath, out var targetFile) && targetFile != null)
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

    private static (CSharpCompilation, SyntaxTree) CreateIsolatedCompilation(string path)
    {
        var tree = ParseFile(path);
        return (BaseCompilation.AddSyntaxTrees(s_globalUsingsTree, tree), tree);
    }

    private static SyntaxTree ParseFile(string path)
    {
#pragma warning disable RS0030
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
