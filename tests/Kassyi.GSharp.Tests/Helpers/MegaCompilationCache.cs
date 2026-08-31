using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Helpers;

public static class MegaCompilationCache
{
    [ModuleInitializer]
    public static void InitializeSadPathTests()
    {
        var testCasesDir = Path.Combine(RoslynTestUtility.GetTestCasesDirectory(), "Analyzers");
        if (Directory.Exists(testCasesDir))
        {
            var files = Directory.GetFiles(testCasesDir, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                var fileName = Path.GetFileName(file);
                RegisterSource(fileName, source);
            }
        }
    }

    private static readonly ConcurrentBag<(string Name, string Source)> s_registeredSources = new();

    public static void RegisterSource(string source)
    {
        s_registeredSources.Add((Guid.NewGuid().ToString() + ".cs", source));
    }

    public static void RegisterSource(string name, string source)
    {
        s_registeredSources.Add((name, source));
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
            .Select(src => CSharpSyntaxTree.ParseText(src.Source, parseOptions, path: src.Name))
            .ToArray();

        return baseComp.AddSyntaxTrees(trees);
    });

    public static CSharpCompilation BaseCompilation => s_baseCompilation.Value;

    private static readonly Lazy<ImmutableArray<Diagnostic>> s_generatorDiagnostics = new(() => {
        var generator = new GpuKernelGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp11));
        driver.RunGeneratorsAndUpdateCompilation(BaseCompilation, out _, out var diagnostics);
        return diagnostics;
    });

    private static readonly Lazy<ImmutableArray<Diagnostic>> s_analyzerDiagnostics = new(() => {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new GpuKernelAnalyzer(),
            new GpuBigIntExprAnalyzer()
            
        );
        var compilationWithAnalyzers = BaseCompilation.WithAnalyzers(analyzers);
        return compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    });

    private static readonly Lazy<ImmutableArray<Diagnostic>> s_compilerDiagnostics = new(() => BaseCompilation.GetDiagnostics());
    public static ImmutableArray<Diagnostic> GetCompilerDiagnosticsForFile(string fileName)
    {
        return s_compilerDiagnostics.Value.Where(d => d.Location.SourceTree?.FilePath?.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true).ToImmutableArray();
    }

    public static ImmutableArray<Diagnostic> GetGeneratorDiagnosticsForFile(string fileName)
    {
        return s_generatorDiagnostics.Value.Where(d => d.Location.SourceTree?.FilePath?.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true).ToImmutableArray();
    }

    public static ImmutableArray<Diagnostic> GetAllDiagnosticsForFile(string fileName)
    {
        return GetCompilerDiagnosticsForFile(fileName)
            .AddRange(GetGeneratorDiagnosticsForFile(fileName))
            .AddRange(GetAnalyzerDiagnosticsForFile(fileName));
    }

    public static ImmutableArray<Diagnostic> GetAnalyzerDiagnosticsForFile(string fileName)
    {
        return s_analyzerDiagnostics.Value.Where(d => d.Location.SourceTree?.FilePath?.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true).ToImmutableArray();
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

#pragma warning disable RS0030 // Do not used banned APIs
    public static Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax ParseStatement(string text)
    {
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(text);
    }

    public static Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax ParseExpression(string text)
    {
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(text);
    }
#pragma warning restore RS0030
}
