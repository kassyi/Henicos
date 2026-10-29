#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs the shuffles of <see cref="WarpShuffleArgumentOrderKernels"/> on CUDA and on both CPU paths, native and managed
/// (#788). The CPU paths split a shuffle into a publish and a read across a state boundary; each argument must still be
/// evaluated once, in the order written.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class WarpShuffleArgumentOrderTests : TranspilerTestBase
{
    private const int WarpSize = 32;

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ShuffleDown_ValueArgumentWithIncrement_IsEvaluatedOnce(bool forceManaged)
    {
        var result = new int[WarpSize];
        var afterV = new int[WarpSize];

        Run(forceManaged, () => WarpShuffleArgumentOrderKernels.LaunchShuffleDownPostIncrementKernel(result, afterV, blockDimX: WarpSize));

        // Lane i publishes (i + 1) * 10 and reads lane i + 1; the last lane has no source and keeps its own value
        Assert.Equal(Enumerable.Range(0, WarpSize).Select(static i => (Math.Min(i + 1, WarpSize - 1) + 1) * 10), result);
        Assert.Equal(Enumerable.Range(0, WarpSize).Select(static i => (i + 1) * 10 + 1), afterV);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_Shuffle_NamedArgumentsOutOfOrder_AreEvaluatedInTheOrderWritten(bool forceManaged)
    {
        var result = new int[WarpSize];
        var orderOut = new int[5];

        Run(forceManaged, () => WarpShuffleArgumentOrderKernels.LaunchShuffleNamedArgSideEffectOrderKernel(result, orderOut, blockDimX: WarpSize));

        // var, width, mask, srcLane: steps 1 to 4, and no step evaluated twice
        Assert.Equal([1, 2, 3, 4, 4], orderOut);
        Assert.All(result, static r => Assert.Equal(101, r));
    }

    // A constant expression stays in place, so it still converts to the uint parameter (a kernel, a device function and
    // a suspending call)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Test_ShuffleDown_ConstantExpressionDelta_ShiftsByItsValue(bool forceManaged, int place)
    {
        var result = new int[WarpSize];

        Run(forceManaged, () =>
        {
            switch (place)
            {
                case 0:
                    WarpShuffleArgumentOrderKernels.LaunchShuffleDownConstantDeltaKernel(result, blockDimX: WarpSize);
                    break;
                case 1:
                    WarpShuffleArgumentOrderKernels.LaunchDeviceFunctionShuffleConstantDeltaKernel(result, blockDimX: WarpSize);
                    break;
                default:
                    WarpShuffleArgumentOrderKernels.LaunchSuspendingCallConstantArgumentKernel(result, blockDimX: WarpSize);
                    break;
            }
        });

        // Half / 2 == 2: lane i reads lane i + 2; the last two lanes have no source and keep their own value
        Assert.Equal(Enumerable.Range(0, WarpSize).Select(static i => i + 2 < WarpSize ? i + 2 : i), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_Shuffle_InWhileCondition_EvaluatesItsValueOncePerIteration(bool forceManaged)
    {
        var rounds = new int[WarpSize];
        var afterV = new int[WarpSize];

        Run(forceManaged, () => WarpShuffleArgumentOrderKernels.LaunchShuffleInWhileConditionKernel(rounds, afterV, blockDimX: WarpSize));

        // Lane 0 publishes 0, 1, 2, 3: three rounds, and four increments of v in every lane
        Assert.All(rounds, static r => Assert.Equal(3, r));
        Assert.Equal(Enumerable.Range(0, WarpSize).Select(static i => i * 100 + 4), afterV);
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
