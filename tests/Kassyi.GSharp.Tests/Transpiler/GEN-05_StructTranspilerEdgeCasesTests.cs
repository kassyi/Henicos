using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_05_StructTranspilerEdgeCasesTests
:  TranspilerTestBase{
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
            new Kassyi.GSharp.Compiler.Dialects.GSharpCudaDialect());
    }


    private static INamedTypeSymbol GetLocalTypeSymbol(string name, [CallerFilePath] string callerFilePath = "")
    {
        var comp = MegaCompilationCache.GetDynamicCompilation(callerFilePath);
        return comp.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>().First();
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_EmptyStruct(ITranspilerDialect dialect)
    {
        var structSym = GetLocalTypeSymbol("EmptyStruct");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("EmptyStruct", sorted[0].Name);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_DeepNesting(ITranspilerDialect dialect)
    {
        var symA = GetLocalTypeSymbol("StructA");
        var symB = GetLocalTypeSymbol("StructB");
        var symC = GetLocalTypeSymbol("StructC");
        var symD = GetLocalTypeSymbol("StructD");

        var context = CreateTestContext(symA, symB, symC, symD);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD", sorted[0].Name);
        Assert.Equal("StructC", sorted[1].Name);
        Assert.Equal("StructB", sorted[2].Name);
        Assert.Equal("StructA", sorted[3].Name);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_ExcludesSystemTypes(ITranspilerDialect dialect)
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
