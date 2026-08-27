using System.Linq;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

internal static class TestHelpers
{
    class Dummy { public void M() {} }

    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        return MegaCompilationCache.GetMethodContext(nameof(TestHelpers), nameof(Dummy), nameof(Dummy.M));
    }
}
