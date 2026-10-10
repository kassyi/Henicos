#pragma warning disable CS0649
namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class StructTranspilerEdgeCasesTests : TranspilerTestBase
{
    private static INamedTypeSymbol GetLocalTypeSymbol(string name, [CallerFilePath] string callerFilePath = "")
    {
        var comp = MegaCompilationCache.GetDynamicCompilation(callerFilePath);
        return comp.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Test_TopologicallySortedStructs_EmptyStruct()
    {
        var structSym = GetLocalTypeSymbol("EmptyStruct");
        var sorted = StructTranspiler.GetTopologicallySortedStructs([structSym]);

        Assert.Single(sorted);
        Assert.Equal("EmptyStruct", sorted[0].Name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Test_TopologicallySortedStructs_DeepNesting()
    {
        var symA = GetLocalTypeSymbol("StructEdgeCases_StructA");
        var symB = GetLocalTypeSymbol("StructEdgeCases_StructB");
        var symC = GetLocalTypeSymbol("StructEdgeCases_StructC");
        var symD = GetLocalTypeSymbol("StructEdgeCases_StructD");

        var sorted = StructTranspiler.GetTopologicallySortedStructs([symA, symB, symC, symD]);

        Assert.Equal(4, sorted.Length);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructEdgeCases_StructD", sorted[0].Name);
        Assert.Equal("StructEdgeCases_StructC", sorted[1].Name);
        Assert.Equal("StructEdgeCases_StructB", sorted[2].Name);
        Assert.Equal("StructEdgeCases_StructA", sorted[3].Name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void Test_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var structSym = GetLocalTypeSymbol("MyStruct");
        var sorted = StructTranspiler.GetTopologicallySortedStructs([structSym]);

        Assert.Single(sorted);
        Assert.Equal("MyStruct", sorted[0].Name);
    }
}

struct EmptyStruct
{
}

struct StructEdgeCases_StructA
{
    public StructEdgeCases_StructB b;
}

struct StructEdgeCases_StructB
{
    public StructEdgeCases_StructC c;
}

struct StructEdgeCases_StructC
{
    public StructEdgeCases_StructD d;
}

struct StructEdgeCases_StructD
{
    public int val;
}

struct MyStruct
{
    public System.DateTime time;
}

