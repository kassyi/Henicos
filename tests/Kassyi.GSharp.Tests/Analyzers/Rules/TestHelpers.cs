using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

internal static class TestHelpers
{
    private const string ID = nameof(TestHelpers);

    public static readonly string TestHelpers_Sources = $$"""
    class Dummy_{{ID}} { public void M() {} }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource(TestHelpers_Sources);

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        return MegaCompilationCache.GetMethodContext($"Dummy_{ID}", "M");
    }
}
