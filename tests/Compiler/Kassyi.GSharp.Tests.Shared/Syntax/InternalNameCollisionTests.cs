using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Verifies that user identifiers named after compiler-internal names neither break compilation nor change results.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class InternalNameCollisionTests : TranspilerTestBase
{
    private const int ThreadCount = 32;

    private static int[] CreateSequence(int start)
    {
        var values = new int[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            values[i] = start + i;
        }

        return values;
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ContextFieldNamedLocals_SurviveBarrier()
    {
        var data = CreateSequence(1);

        InternalNameCollisionKernels.LaunchContextFieldNamedLocalsKernel(data, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            Assert.Equal(5 * value + 10, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AggregateTempNamedLocal_IsReadByObjectInitializer()
    {
        var data = CreateSequence(1);

        InternalNameCollisionKernels.LaunchAggregateTempNamedLocalKernel(data, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            Assert.Equal(value * 100 + value + 1, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TupleTempNamedLocals_AreSwapped()
    {
        var first = CreateSequence(1);
        var second = CreateSequence(1000);

        InternalNameCollisionKernels.LaunchTupleTempNamedLocalsKernel(first, second, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(1000 + i, first[i]);
            Assert.Equal(1 + i, second[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LoweringLabelNamedLabels_CoexistWithLoweredControlFlow()
    {
        const int Rounds = 2;
        var data = CreateSequence(1);

        InternalNameCollisionKernels.LaunchLoweringLabelNamedLabelsKernel(data, Rounds, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(Rounds * (i + 1), data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_WarpTempNamedLocal_SurvivesBarrierBeforeShuffle()
    {
        var data = CreateSequence(1);

        InternalNameCollisionKernels.LaunchWarpTempNamedLocalKernel(data, blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            int value = i + 1;
            // Lanes shifted past the warp edge keep their own value.
            int shifted = i < ThreadCount - 1 ? value + 1 : value;
            Assert.Equal(value * 1000 + shifted, data[i]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LauncherLocalNamedParameters_ArePassedThrough()
    {
        var data = new int[ThreadCount];

        InternalNameCollisionKernels.LaunchLauncherLocalNamedParametersKernel(data, 1, 2, 3, 4, 5, 6, 7, 8, 9,
            blockDimX: ThreadCount);

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(45, data[i]);
        }
    }
}
