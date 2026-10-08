#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

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
        var mathDllPath = Path.Combine(Path.GetDirectoryName(coreDllPath) ?? baseDir, "Kassyi.GSharp.Stdlib.GpuMath.dll");
        if (File.Exists(mathDllPath) && !references.Any(r => string.Equals(r.Display, mathDllPath, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(mathDllPath)));
        }

        var baseComp = CSharpCompilation.Create("MegaTestCompilationBase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(references);

        return baseComp;
    });

    public static CSharpCompilation BaseCompilation => s_baseCompilation.Value;

    private static CSharpCompilation? s_dynamicCompilation;
    private static readonly object s_compilationLock = new();
    private static readonly ConcurrentDictionary<string, Lazy<SyntaxTree>> s_cachedSyntaxTrees = new();
    private static readonly HashSet<string> s_includedFiles = new();

    private static CSharpCompilation GetOrCreateDynamicCompilationLocked()
    {
        s_dynamicCompilation ??= BaseCompilation;
        return s_dynamicCompilation;
    }

    public static CSharpCompilation GetDynamicCompilation([CallerFilePath] string callerFilePath = "")
    {
        if (string.IsNullOrEmpty(callerFilePath))
        {
            lock (s_compilationLock)
            {
                return GetOrCreateDynamicCompilationLocked();
            }
        }

        var lazyTree = s_cachedSyntaxTrees.GetOrAdd(callerFilePath, static path => new Lazy<SyntaxTree>(() =>
        {
            var text = File.ReadAllText(path);
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
#pragma warning disable RS0030
            return CSharpSyntaxTree.ParseText(text, parseOptions, path: path);
#pragma warning restore RS0030
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        var syntaxTree = lazyTree.Value;

        lock (s_compilationLock)
        {
            var current = GetOrCreateDynamicCompilationLocked();

            if (s_includedFiles.Add(callerFilePath))
            {
                current = current.AddSyntaxTrees(syntaxTree);
                s_dynamicCompilation = current;
            }

            return current;
        }
    }

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        var compilation = GetDynamicCompilation(callerFilePath);

        var typeSymbol = compilation.GetTypeByMetadataName(targetType.FullName ?? targetType.Name)
                         ?? compilation.GetSymbolsWithName(targetType.Name, SymbolFilter.Type)
                             .OfType<INamedTypeSymbol>().FirstOrDefault()
                         ?? throw new InvalidOperationException(
                             $"Type {targetType.FullName} not found in the dynamic compilation.");

        var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault()
                           ?? throw new InvalidOperationException(
                               $"Method {methodName} not found in {typeSymbol.Name}.");

        var syntaxTree = methodSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        var model = compilation.GetSemanticModel(syntaxTree);
        return (model, methodSymbol);
    }

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext<TTarget>(
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        return GetMethodContext(typeof(TTarget), methodName, callerFilePath);
    }

    public static INamedTypeSymbol GetNamedTypeSymbol(string typeName)
    {
        var symbol = BaseCompilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
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
        var model = BaseCompilation.GetSemanticModel(syntaxTree);
        return (model, methodSymbol);
    }
}

