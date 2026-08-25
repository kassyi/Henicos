using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Syn46_50_ConcurrencyAndWarpTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    [Trait("FeatureId", "SYN-68")]
    public void TestIntrinsicsKernel()
    {
        int[] dataArray = new int[2];
        dataArray[0] = 0; // atomic sum
        dataArray[1] = 0; // max value

        Syn46_50_ConcurrencyAndWarpKernelsWrapper.Launch_IntrinsicsKernel(dataArray, blockDimX: 10);

        Assert.Equal(45, dataArray[0]); // Sum of 0..9 is 45
        Assert.Equal(100, dataArray[1]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    public void TestAtomicAddStress_HighContention()
    {
        int[] globalSum = [0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        int expectedTotalThreads = (int)(threadsPerBlock * blocksPerGrid); // 4096 threads

        Syn46_50_ConcurrencyAndWarpKernelsWrapper.Launch_AtomicAddStressKernel(
            globalSum,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        Assert.Equal(expectedTotalThreads, globalSum[0]);
    }

    [Fact(Skip = "未実装: SYN-46 (ワープシャッフル・同期プリミティブ)")]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_WarpPrimitives_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-48 (グリッド協調グループ同期 CooperativeGroups)")]
    [Trait("FeatureId", "SYN-48")]
    [Trait("Category", "Syntax")]
    public void Test_SYN48_CooperativeGroups_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-50 (非同期コピー / TMA Tensor Memory Accelerator)")]
    [Trait("FeatureId", "SYN-50")]
    [Trait("Category", "Syntax")]
    public void Test_SYN50_AsyncCopyTma_Transpilation()
    {
    }
}

