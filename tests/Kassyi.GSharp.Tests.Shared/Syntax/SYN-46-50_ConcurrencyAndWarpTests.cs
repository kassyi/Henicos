
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_46_50_ConcurrencyAndWarpTests
:  TranspilerTestBase{
    [SkippableFact]
    [Trait("FeatureId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_WarpPrimitives_Transpilation()
    {
    }
    [SkippableFact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemoryKernel()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        int[] dataArray = [1, 2, 3, 4];

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_SharedMemoryKernel(dataArray, 4, blockDimX: 4, sharedMemBytes: 32);

        Assert.Equal(3, dataArray[0]);
        Assert.Equal(6, dataArray[1]);
        Assert.Equal(9, dataArray[2]);
        Assert.Equal(12, dataArray[3]);
    }

    [SkippableFact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-47")]
    public void TestSharedMemory_WarpSyncAcross256Threads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int size = 256;
        int[] dataArray = new int[size];
        for (int i = 0; i < size; i++)
        {
            dataArray[i] = i * 3 + 1;
        }

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_SharedMemoryReverseKernel(
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

    [SkippableFact]
    [Trait("FeatureId", "SYN-48")]
    [Trait("Category", "Syntax")]
    public void Test_SYN48_CooperativeGroups_Transpilation()
    {
    }

    [SkippableFact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    [Trait("FeatureId", "SYN-68")]
    public void TestIntrinsicsKernel()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] dataArray = new int[2];
        dataArray[0] = 0; // atomic sum
        dataArray[1] = 0; // max value

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_IntrinsicsKernel(dataArray, blockDimX: 10);

        Assert.Equal(45, dataArray[0]); // Sum of 0..9 is 45
        Assert.Equal(100, dataArray[1]);
    }

    [SkippableFact]
    [Trait("Category", "Execution")]
    [Trait("Category", "Stress")]
    [Trait("FeatureId", "SYN-49")]
    public void TestAtomicAddStress_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        int[] globalSum = [0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        int expectedTotalThreads = (int)(threadsPerBlock * blocksPerGrid); // 4096 threads

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_AtomicAddStressKernel(
            globalSum,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        Assert.Equal(expectedTotalThreads, globalSum[0]);
    }

    [SkippableFact]
    [Trait("Category", "Execution")]
    [Trait("Category", "Stress")]
    [Trait("FeatureId", "SYN-49")]
    public void TestAtomicOrMaxStress_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        uint[] globalState = [0, 0];
        uint threadsPerBlock = 256;
        uint blocksPerGrid = 16;
        uint expectedTotalThreads = threadsPerBlock * blocksPerGrid; // 4096 threads

        SYN_46_50_ConcurrencyAndWarpKernelsWrapper.Launch_AtomicOrMaxStressKernel(
            globalState,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        // First 31 threads will OR their bits: 1 << 0 to 1 << 30
        uint expectedOr = int.MaxValue; // 0x7FFFFFFF
        Assert.Equal(expectedOr, globalState[0]);

        // Max thread ID will be 4095
        Assert.Equal(expectedTotalThreads - 1, globalState[1]);
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-50")]
    [Trait("Category", "Syntax")]
    public void Test_SYN50_AsyncCopyTma_Transpilation()
    {
    }
}
