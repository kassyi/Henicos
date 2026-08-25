using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn11_16_CollectionsAndMemoryTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemoryKernel()
    {
        int[] dataArray = [1, 2, 3, 4];

        Syn11_16_CollectionsAndMemoryKernelsWrapper.Launch_SharedMemoryKernel(dataArray, 4, blockDimX: 4, sharedMemBytes: 32);

        Assert.Equal(3, dataArray[0]);
        Assert.Equal(6, dataArray[1]);
        Assert.Equal(9, dataArray[2]);
        Assert.Equal(12, dataArray[3]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemory_WarpSyncAcross256Threads()
    {
        const int size = 256;
        int[] dataArray = new int[size];
        for (int i = 0; i < size; i++)
        {
            dataArray[i] = i * 3 + 1;
        }

        Syn11_16_CollectionsAndMemoryKernelsWrapper.Launch_SharedMemoryReverseKernel(
            dataArray,
            size,
            blockDimX: size
        );

        for (int i = 0; i < size; i++)
        {
            int expected = (size - 1 - i) * 3 + 1;
            Assert.Equal(expected, dataArray[i]);
        }
    }

    [Fact(Skip = "未実装: SYN-11 (Index & Range 構文 ^1, [1..5])")]
    [Trait("FeatureId", "SYN-11")]
    [Trait("Category", "Syntax")]
    public void Test_SYN11_IndexAndRange_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-12 (コレクション式 [1, 2, 3] / スプレッド ..)")]
    [Trait("FeatureId", "SYN-12")]
    [Trait("Category", "Syntax")]
    public void Test_SYN12_CollectionExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-13 (stackalloc と固定長スタック配列)")]
    [Trait("FeatureId", "SYN-13")]
    [Trait("Category", "Syntax")]
    public void Test_SYN13_Stackalloc_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-15 (Span<T> / ReadOnlySpan<T> のカーネル内使用)")]
    [Trait("FeatureId", "SYN-15")]
    [Trait("Category", "Syntax")]
    public void Test_SYN15_SpanUsage_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-16 (2次元インデクサ / 多次元配列)")]
    [Trait("FeatureId", "SYN-16")]
    [Trait("Category", "Syntax")]
    public void Test_SYN16_MultidimensionalIndexer_Transpilation()
    {
    }
}

