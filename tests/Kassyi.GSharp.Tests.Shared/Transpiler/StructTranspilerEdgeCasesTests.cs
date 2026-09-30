#pragma warning disable CS0649
namespace Kassyi.GSharp.Tests.Transpiler;

public class StructTranspilerEdgeCasesTests
:  TranspilerTestBase{
    private static TranspileContext CreateTestContext(params INamedTypeSymbol[] usedStructsArray)
    {
        var usedStructs = new HashSet<INamedTypeSymbol>(usedStructsArray, SymbolEqualityComparer.Default);
        return new TranspileContext(
            default,
            usedStructs,
            new OverrideScopeStack(),
            null!,
            null,
            null!,
            ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty,
            new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect(),
            Kassyi.GSharp.Compiler.Transpiler.Rules.TranspilerRuleRegistry.CreateDefault());
    }


    private static INamedTypeSymbol GetLocalTypeSymbol(string name, [CallerFilePath] string callerFilePath = "")
    {
        var comp = MegaCompilationCache.GetDynamicCompilation(callerFilePath);
        return comp.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Test_TopologicallySortedStructs_EmptyStruct()
    {
        var structSym = GetLocalTypeSymbol("EmptyStruct");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("EmptyStruct", sorted[0].Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Test_TopologicallySortedStructs_DeepNesting()
    {
        var comp = MegaCompilationCache.GetDynamicCompilation();
        var symA = comp.GetSymbolsWithName("StructA", SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
        var symB = comp.GetSymbolsWithName("StructB", SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
        var symC = comp.GetSymbolsWithName("StructC", SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
        var symD = comp.GetSymbolsWithName("StructD", SymbolFilter.Type).OfType<INamedTypeSymbol>().First();

        var context = CreateTestContext(symA, symB, symC, symD);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD", sorted[0].Name);
        Assert.Equal("StructC", sorted[1].Name);
        Assert.Equal("StructB", sorted[2].Name);
        Assert.Equal("StructA", sorted[3].Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Test_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var structSym = GetLocalTypeSymbol("MyStruct");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("MyStruct", sorted[0].Name);
    }
}

struct EmptyStruct
{
}

struct StructA
{
    public StructB b;
}

struct StructB
{
    public StructC c;
}

struct StructC
{
    public StructD d;
}

struct StructD
{
    public int val;
}

struct MyStruct
{
    public System.DateTime time;
}

