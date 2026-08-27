using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using System.Collections.Generic;
using System.Collections.Immutable;
using Kassyi.GSharp.Tests.Helpers;

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

    struct Empty { }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_EmptyStruct()
    {
        var structSym = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(Empty));
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("Empty", sorted[0].Name);
    }

    struct StructA { public StructB b; }
    struct StructB { public StructC c; }
    struct StructC { public StructD d; }
    struct StructD { public int val; }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_DeepNesting()
    {
        var symA = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(StructA));
        var symB = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(StructB));
        var symC = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(StructC));
        var symD = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(StructD));

        var context = CreateTestContext(symA, symB, symC, symD);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD", sorted[0].Name);
        Assert.Equal("StructC", sorted[1].Name);
        Assert.Equal("StructB", sorted[2].Name);
        Assert.Equal("StructA", sorted[3].Name);
    }

    struct MyStruct { public DateTime time; }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var structSym = MegaCompilationCache.GetNamedTypeSymbol(nameof(GEN_05_StructTranspilerEdgeCasesTests), nameof(MyStruct));
        var context = CreateTestContext(structSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("MyStruct", sorted[0].Name);
    }
}
