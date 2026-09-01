#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Helpers;

public static class MegaCompilationCache
{
    private static readonly ConcurrentBag<string> s_registeredSources = new();

    public static void RegisterSource(string source)
    {
        s_registeredSources.Add(source);
    }

    private static readonly Lazy<CSharpCompilation> s_baseCompilation = new(() =>
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var references = trustedAssemblies!
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        var baseComp = CSharpCompilation.Create("MegaTestCompilationBase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(references);

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var trees = s_registeredSources
            .Select((src, i) => CSharpSyntaxTree.ParseText(src, parseOptions, path: $"MegaTestSources_{i}.cs"))
            .ToArray();

        return baseComp.AddSyntaxTrees(trees);
    });

    public static CSharpCompilation BaseCompilation => s_baseCompilation.Value;

    private static CSharpCompilation? s_dynamicCompilation;
    private static readonly object s_compilationLock = new();
    private static readonly ConcurrentDictionary<string, SyntaxTree> s_cachedSyntaxTrees = new();
    private static readonly HashSet<string> s_includedFiles = new();

    public static CSharpCompilation GetDynamicCompilation([CallerFilePath] string callerFilePath = "")
    {
        if (string.IsNullOrEmpty(callerFilePath))
        {
            return s_dynamicCompilation ?? BaseCompilation;
        }

        var syntaxTree = s_cachedSyntaxTrees.GetOrAdd(callerFilePath, static path =>
        {
            var text = File.ReadAllText(path);
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
            return CSharpSyntaxTree.ParseText(text, parseOptions, path: path);
        });

        lock (s_compilationLock)
        {
            s_dynamicCompilation ??= BaseCompilation;

            if (s_includedFiles.Add(callerFilePath))
            {
                s_dynamicCompilation = s_dynamicCompilation.AddSyntaxTrees(syntaxTree);
            }

            return s_dynamicCompilation;
        }
    }

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext(
        Type targetType,
        string methodName,
        [CallerFilePath] string callerFilePath = "")
    {
        var compilation = GetDynamicCompilation(callerFilePath);

        var typeSymbol = compilation.GetTypeByMetadataName(targetType.FullName ?? targetType.Name) 
                         ?? compilation.GetSymbolsWithName(targetType.Name, SymbolFilter.Type).OfType<INamedTypeSymbol>().FirstOrDefault()
                         ?? throw new InvalidOperationException($"Type {targetType.FullName} not found in the dynamic compilation.");

        var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault()
                           ?? throw new InvalidOperationException($"Method {methodName} not found in {typeSymbol.Name}.");

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
