namespace Kassyi.GSharp.Tests.Transpiler;

public class TranspilerInfrastructureTests
:  TranspilerTestBase{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_ForwardDeclarations_And_MutualCalls()
    {
        int[] counts = [1, 2, 3];
        int[] vals = [5, 5, 5];
        int[] results = [0, 0, 0];

        TranspilerInfrastructureKernels.LaunchMutualCallKernel(counts, vals, results, blockDimX: 3);

        // thread 0: count=1, val=5 -> MutualFuncA(1, 5) -> MutualFuncB(0, 15) -> returns 15
        Assert.Equal(15, results[0]);
        // thread 1: count=2, val=5 -> MutualFuncA(2, 5) -> MutualFuncB(1, 15) -> MutualFuncA(0, 35) -> returns 35
        Assert.Equal(35, results[1]);
        // thread 2: count=3, val=5 -> MutualFuncA(3, 5) -> MutualFuncB(2, 15) -> MutualFuncA(1, 35) -> MutualFuncB(0, 45) -> returns 45
        Assert.Equal(45, results[2]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_OverloadedFunctions_DeduplicationAndForwardDecl()
    {
        int[] inputA = [3, 4];
        int[] inputB = [5, 6];
        int[] inputC = [7, 8];
        int[] output2Arg = [0, 0];
        int[] output3Arg = [0, 0];

        TranspilerInfrastructureKernels.LaunchOverloadTestKernel(
            inputA, inputB, inputC, output2Arg, output3Arg, blockDimX: 2);

        // thread 0: 2-arg MultiplyAdd(3, 5) = 15; 3-arg MultiplyAdd(3, 5, 7) = 22
        Assert.Equal(15, output2Arg[0]);
        Assert.Equal(22, output3Arg[0]);

        // thread 1: 2-arg MultiplyAdd(4, 6) = 24; 3-arg MultiplyAdd(4, 6, 8) = 32
        Assert.Equal(24, output2Arg[1]);
        Assert.Equal(32, output3Arg[1]);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_PinnedArrayTransfer_EmptyAndHugeArrays()
    {
        // 1. Empty array transfer
        int[] emptyData = [];
        TranspilerInfrastructureKernels.LaunchArrayTransferKernel(emptyData, 0, blockDimX: 1);
        Assert.Empty(emptyData);

        // 2. Large array transfer (65,536 elements across 256 blocks of 256 threads)
        int length = 65536;
        int[] hugeData = new int[length];
        for (int i = 0; i < length; i++)
        {
            hugeData[i] = i;
        }

        TranspilerInfrastructureKernels.LaunchArrayTransferKernel(
            hugeData, length, blockDimX: 256, gridDimX: 256);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(i + 42, hugeData[i]);
        }
    }
}
