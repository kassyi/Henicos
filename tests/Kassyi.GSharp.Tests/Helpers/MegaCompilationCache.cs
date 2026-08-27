using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Helpers;

public static class MegaCompilationCache
{
    private static CSharpCompilation s_baseCompilation;

    public static CSharpCompilation BaseCompilation
    {
        get
        {
            return s_baseCompilation
                   ?? throw new InvalidOperationException(
                       "Mega compilation base cache was not initialized.");
        }
    }

    [ModuleInitializer]
    public static void Initialize()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var references = trustedAssemblies!
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        s_baseCompilation = CSharpCompilation.Create("MegaTestCompilationBase")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddReferences(references);
    }

    private static readonly ConcurrentDictionary<string, CSharpCompilation> s_fileCompilations = new();

    public static CSharpCompilation GetCompilationForFile([CallerFilePath] string callerFilePath = "")
    {
        if (string.IsNullOrEmpty(callerFilePath))
            throw new ArgumentException("Caller file path must be provided.");

        return s_fileCompilations.GetOrAdd(callerFilePath, path =>
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path: path);
            return BaseCompilation.AddSyntaxTrees(tree);
        });
    }

    public static INamedTypeSymbol GetNamedTypeSymbol(string className, string targetName,
        [CallerFilePath] string callerFilePath = "")
    {
        var compilation = GetCompilationForFile(callerFilePath);
        var fullName = className + "." + targetName;

        var symbol = compilation.GetSymbolsWithName(targetName, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(s => s.ToDisplayString().EndsWith(fullName));

        return symbol ?? throw new InvalidOperationException($"Type {fullName} not found in file {callerFilePath}.");
    }

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) GetMethodContext(string className,
        string targetName, string methodName, [CallerFilePath] string callerFilePath = "")
    {
        var typeSymbol = GetNamedTypeSymbol(className, targetName, callerFilePath);

        var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault()
                           ?? throw new InvalidOperationException(
                               $"Method {methodName} not found in {typeSymbol.Name}.");

        var syntaxTree = methodSymbol.DeclaringSyntaxReferences[0].SyntaxTree;
        var model = GetCompilationForFile(callerFilePath).GetSemanticModel(syntaxTree);
        return (model, methodSymbol);
    }
}
