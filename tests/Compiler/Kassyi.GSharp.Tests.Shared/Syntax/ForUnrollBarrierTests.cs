using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution tests for <c>Gpu.ForUnroll</c> loops whose bodies contain barriers or suspending device function calls (#559).
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class ForUnrollBarrierTests : TranspilerTestBase
{
    private const uint BlockSize = 4;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExchangeKernel_SynchronizesAcrossIterations()
    {
        int[] buffer = new int[BlockSize];
        int[] output = new int[BlockSize];

        ForUnrollBarrierKernels.LaunchExchangeKernel(buffer, output, blockDimX: BlockSize);

        Assert.Equal([33, 36, 39, 30], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefContextKernel_PropagatesAccumulatorAcrossBarriers()
    {
        int[] buffer = new int[BlockSize];
        int[] output = new int[BlockSize];

        ForUnrollBarrierKernels.LaunchRefContextKernel(buffer, output, blockDimX: BlockSize);

        Assert.Equal([6, 12, 18, 0], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroCountKernel_SkipsBodyWithoutCorruptingOutput()
    {
        int[] buffer = new int[BlockSize];
        int[] output = [-1, -1, -1, -1];

        ForUnrollBarrierKernels.LaunchZeroCountKernel(buffer, output, blockDimX: BlockSize);

        Assert.Equal([-1, -1, -1, -1], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedForUnrollKernel_ExpandsOuterAndUnrollsInner()
    {
        int[] buffer = new int[BlockSize];
        int[] output = new int[BlockSize];

        ForUnrollBarrierKernels.LaunchNestedForUnrollKernel(buffer, output, blockDimX: BlockSize);

        Assert.Equal([48, 50, 52, 46], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_MegaReducedKernel_ShiftsAndIncrementsAcrossThreeRounds()
    {
        int[] input = [10, 20, 30, 40];
        int[] scratch = new int[BlockSize];
        int[] temp = new int[BlockSize];

        ForUnrollBarrierKernels.LaunchMegaReducedKernel(input, scratch, temp, (int)BlockSize, blockDimX: BlockSize);

        Assert.Equal([43, 13, 23, 33], temp);
    }
}
