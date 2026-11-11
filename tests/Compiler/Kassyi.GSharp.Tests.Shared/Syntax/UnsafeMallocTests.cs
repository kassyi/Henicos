namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class UnsafeMallocTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void MallocRoundTrip_FourThreads_WritesReadsAndFreesPerThread()
    {
        int[] output = new int[4];

        UnsafeMallocKernels.LaunchMallocRoundTripKernel(output, blockDimX: 4);

        // Thread t sums t*10 + 0..3
        Assert.Equal([6, 46, 86, 126], output);
    }
}
