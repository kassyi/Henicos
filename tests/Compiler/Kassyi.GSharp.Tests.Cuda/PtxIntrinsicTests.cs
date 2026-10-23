namespace Kassyi.GSharp.Tests.Cuda;

public class PtxIntrinsicTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MultiplyAddWide_KeepsFull64BitProduct()
    {
        ulong[] unsignedResult = [0ul];
        long[] signedResults = [0L, 0L];

        PtxIntrinsicKernels.LaunchMultiplyAddWideKernel(unsignedResult, signedResults, blockDimX: 1);

        // (2^32 - 1)^2 + 5
        Assert.Equal(0xFFFFFFFE_00000006ul, unsignedResult[0]);
        Assert.Equal(-14L, signedResults[0]);
        // (-2^31)^2
        Assert.Equal(0x40000000_00000000L, signedResults[1]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MultiplyAddWideWithCarry_PropagatesCarry()
    {
        ulong[] results = [0ul, 0ul, 0ul];
        ulong[] carries = [9ul, 9ul, 9ul];

        PtxIntrinsicKernels.LaunchMultiplyAddWideWithCarryKernel(results, carries, blockDimX: 1);

        // 0xFFFFFFFE00000001 + 0xFFFFFFFF00000000 + 1 = 2^64 + 0xFFFFFFFD00000002
        Assert.Equal(0xFFFFFFFD_00000002ul, results[0]);
        Assert.Equal(1ul, carries[0]);

        Assert.Equal(17ul, results[1]);
        Assert.Equal(0ul, carries[1]);

        // 1 + (2^64 - 2) + 1 = 2^64
        Assert.Equal(0ul, results[2]);
        Assert.Equal(1ul, carries[2]);
    }
}
