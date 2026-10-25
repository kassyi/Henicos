namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Verifies that by-reference parameters in device functions re-read memory after another thread writes across a barrier
/// or after the same thread writes through an aliased array element.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class RefParameterAliasTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefGlobalBarrierNeighborWrite_RereadsUpdatedValueAfterBarrier()
    {
        const int count = RefParameterAliasKernels.N;
        int[] buf = [10, 11, 12, 13];
        var output = new int[count * 2];

        RefParameterAliasKernels.LaunchRefGlobalBarrierNeighborWriteKernel(buf, output, blockDimX: count);

        int[] expected = [10, 100, 11, 101, 12, 102, 13, 103];
        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefGlobalNoBarrierSelfWrite_RereadsUpdatedValueAfterArrayWrite()
    {
        const int count = RefParameterAliasKernels.N;
        int[] buf = [10, 11, 12, 13];
        var output = new int[count * 2];

        RefParameterAliasKernels.LaunchRefGlobalNoBarrierSelfWriteKernel(buf, output, blockDimX: count);

        int[] expected = [10, 200, 11, 201, 12, 202, 13, 203];
        Assert.Equal(expected, output);
    }
}
