
namespace Kassyi.GSharp.Tests.Analyzers.Rules;

internal static class TestHelpers
{
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        return MegaCompilationCache.GetMethodContext<Dummy_TestHelpers>("M");
    }
}

class Dummy_TestHelpers { public void M() {} }
