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
        var symA = GetLocalTypeSymbol("StructA");
        var symB = GetLocalTypeSymbol("StructB");
        var symC = GetLocalTypeSymbol("StructC");
        var symD = GetLocalTypeSymbol("StructD");

        var sorted = StructTranspiler.GetTopologicallySortedStructs([symA, symB, symC, symD]);

        Assert.Equal(4, sorted.Length);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD", sorted[0].Name);
        Assert.Equal("StructC", sorted[1].Name);
        Assert.Equal("StructB", sorted[2].Name);
        Assert.Equal("StructA", sorted[3].Name);
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

