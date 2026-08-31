using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public class GEN_23_31_TypeSystemAndStructTests
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

    [Fact]
    [Trait("FeatureId", "GEN-23")]
    [Trait("Category", "Syntax")]
    public void Test_GEN23_FixedStatement_EmitsRestrictAndMultiVariable()
    {
        var cudaSource = GEN_23_31_TypeSystemAndStructKernelsWrapper.FixedRestrictKernel_CudaSource;
        // Verify __restrict__ modifier on array pointer parameters
        Assert.Contains("int* __restrict__ src", cudaSource);
        Assert.Contains("int* __restrict__ dest", cudaSource);

        // Verify multiple fixed variables are transpiled
        Assert.Contains("int* pSrc=&src[0];", cudaSource);
        Assert.Contains("int* pDest=&dest[0];", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "GEN-28")]
    [Trait("Category", "Execution")]
    public void Test_GEN28_DynamicSharedMemory_MultiType_NoMemoryException()
    {
        int[] output = [0];
        int count1 = 10;
        int count2 = 5;

        // Dynamic shared memory allocation size in bytes with alignment padding allowance
        uint smemBytes = (uint)(count1 * sizeof(byte) + count2 * sizeof(double) + 64);

        GEN_23_31_TypeSystemAndStructKernelsWrapper.Launch_DynamicSharedMemoryMultiTypeKernel(
            output, count1, count2, blockDimX: 32, sharedMemBytes: smemBytes);

        // smemBytes[0] = 1, smemDoubles[0] = 2.5 (cast to int is 2), sum = 3
        Assert.Equal(3, output[0]);
    }

    [Fact]
    [Trait("FeatureId", "GEN-30")]
    [Trait("Category", "Execution")]
    public void Test_GEN30_InlineArray10_ExecutesWithoutMemoryCorruption()
    {
        int[] output = [0];

        GEN_23_31_TypeSystemAndStructKernelsWrapper.Launch_InlineArrayKernel(output, blockDimX: 1);

        // Sum of (1..10)*10 = (10+20+30+40+50+60+70+80+90+100) = 550
        Assert.Equal(550, output[0]);
    }

    [Fact]
    [Trait("FeatureId", "GEN-31")]
    [Trait("Category", "Syntax")]
    public void Test_GEN31_ReversedStructDependencies_TopologicallySorted()
    {
        var parentSym = MegaCompilationCache.GetNamedTypeSymbol("ParentStruct");
        var childSym = MegaCompilationCache.GetNamedTypeSymbol("ChildStruct");

        // Pass ParentStruct first (reversed dependency order)
        var context = CreateTestContext(parentSym, childSym);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(2, sorted.Count);
        Assert.Equal("ChildStruct", sorted[0].Name);
        Assert.Equal("ParentStruct", sorted[1].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-28")]
    [Trait("Category", "Alignment")]
    [Trait("Category", "Execution")]
    public void Test_GEN28_EdgeCase_ByteDoubleAlignmentValidation()
    {
        // 1. Explicit Layout Validation
        Assert.Equal(24, Unsafe.SizeOf<PaddedByteDoubleStruct>());
        Assert.Equal(8, Marshal.OffsetOf<PaddedByteDoubleStruct>("ValueD").ToInt32());

        // 2. Empty Array Transfer (Should not crash)
        PaddedByteDoubleStruct[] emptyItems = [];
        GEN_23_31_TypeSystemAndStructKernelsWrapper.Launch_PaddedByteDoubleKernel(emptyItems, 0, blockDimX: 1);

        // 3. Single Element Data Validation
        PaddedByteDoubleStruct[] items = [
            new PaddedByteDoubleStruct { Flag1 = 1, ValueD = 2.0, ValueI = 100, Flag2 = 10 }
        ];

        GEN_23_31_TypeSystemAndStructKernelsWrapper.Launch_PaddedByteDoubleKernel(items, 1, blockDimX: 1);

        Assert.Equal((byte)2, items[0].Flag1);
        Assert.Equal(4.0, items[0].ValueD);
        Assert.Equal(110, items[0].ValueI);
        Assert.Equal((short)9, items[0].Flag2);
    }
}
