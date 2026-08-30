using Kassyi.GSharp.Compiler.Transpiler;

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
            ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty);
    }


    private const string ID = nameof(GEN_05_StructTranspilerEdgeCasesTests);

    public static readonly string GEN05_Sources = $$"""
    struct Empty_{{ID}} { }
    
    struct StructA_{{ID}} { public StructB_{{ID}} b; }
    struct StructB_{{ID}} { public StructC_{{ID}} c; }
    struct StructC_{{ID}} { public StructD_{{ID}} d; }
    struct StructD_{{ID}} { public int val; }

    struct MyStruct_{{ID}} { public System.DateTime time; }
    """;

    [ModuleInitializer]
    public static void Register() => MegaCompilationCache.RegisterSource("GEN-05_StructTranspilerEdgeCasesTests.cs", GEN05_Sources);

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_EmptyStruct()
    {
        var structSym = MegaCompilationCache.GetNamedTypeSymbol($"Empty_{ID}");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal($"Empty_{ID}", sorted[0].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_DeepNesting()
    {
        var symA = MegaCompilationCache.GetNamedTypeSymbol($"StructA_{ID}");
        var symB = MegaCompilationCache.GetNamedTypeSymbol($"StructB_{ID}");
        var symC = MegaCompilationCache.GetNamedTypeSymbol($"StructC_{ID}");
        var symD = MegaCompilationCache.GetNamedTypeSymbol($"StructD_{ID}");

        var context = CreateTestContext(symA, symB, symC, symD);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal($"StructD_{ID}", sorted[0].Name);
        Assert.Equal($"StructC_{ID}", sorted[1].Name);
        Assert.Equal($"StructB_{ID}", sorted[2].Name);
        Assert.Equal($"StructA_{ID}", sorted[3].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var structSym = MegaCompilationCache.GetNamedTypeSymbol($"MyStruct_{ID}");
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal($"MyStruct_{ID}", sorted[0].Name);
    }
}
