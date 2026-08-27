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
