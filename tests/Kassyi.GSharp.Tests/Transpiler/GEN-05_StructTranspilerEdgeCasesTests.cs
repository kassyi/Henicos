
using Kassyi.GSharp.Compiler.Transpiler.Dialects;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_05_StructTranspilerEdgeCasesTests
{
    private static TranspileContext CreateTestContext(params INamedTypeSymbol[] usedStructsArray)
    {
        var usedStructs = new HashSet<INamedTypeSymbol>(usedStructsArray, SymbolEqualityComparer.Default);
        return new TranspileContext(
            default,
            usedStructs,
            new Dictionary<string, OverrideValue>(),
            null!,
            null,
            null!,
            ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty,
            new GSharpCudaDialect());
    }


    private static INamedTypeSymbol GetLocalTypeSymbol(string name, [CallerFilePath] string callerFilePath = "")
    {
        var comp = MegaCompilationCache.GetDynamicCompilation(callerFilePath);
        return comp.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_EmptyStruct()
    {
        var structSym = GetLocalTypeSymbol("EmptyStruct_GEN05");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("EmptyStruct_GEN05", sorted[0].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_DeepNesting()
    {
        var symA = GetLocalTypeSymbol("StructA_GEN05");
        var symB = GetLocalTypeSymbol("StructB_GEN05");
        var symC = GetLocalTypeSymbol("StructC_GEN05");
        var symD = GetLocalTypeSymbol("StructD_GEN05");

        var context = CreateTestContext(symA, symB, symC, symD);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD_GEN05", sorted[0].Name);
        Assert.Equal("StructC_GEN05", sorted[1].Name);
        Assert.Equal("StructB_GEN05", sorted[2].Name);
        Assert.Equal("StructA_GEN05", sorted[3].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var structSym = GetLocalTypeSymbol("MyStruct_GEN05");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("MyStruct_GEN05", sorted[0].Name);
    }
}

struct EmptyStruct_GEN05
{
}

struct StructA_GEN05
{
    public StructB_GEN05 b;
}

struct StructB_GEN05
{
    public StructC_GEN05 c;
}

struct StructC_GEN05
{
    public StructD_GEN05 d;
}

struct StructD_GEN05
{
    public int val;
}

struct MyStruct_GEN05
{
    public System.DateTime time;
}
