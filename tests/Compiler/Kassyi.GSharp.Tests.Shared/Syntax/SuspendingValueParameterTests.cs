using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution tests for writes into a value parameter of a device function that suspends (#455): the write must survive
/// the barrier on every dialect.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class SuspendingValueParameterTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructFieldWrite_SurvivesBarrier() => AssertStructFieldWrite();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedFieldWrite_SurvivesBarrier() => AssertNestedFieldWrite();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_InlineArrayElementWrite_SurvivesBarrier() => AssertInlineArrayElementWrite();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PrefixIncrementOfField_SurvivesBarrier() => AssertPrefixIncrementField();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_FieldPassedByRef_SurvivesBarrier() => AssertRefFieldArgument();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArrayElementWrite_IsVisibleAfterBarrierAndToCaller() => AssertArrayElementWrite();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeconstructingSwapOfFields_SurvivesBarrier() => AssertTupleSwapFields();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeconstructingSwapOfScalars_SurvivesBarrier() => AssertTupleSwapScalars();

    internal static void AssertTupleSwapFields()
    {
        var output = new float[BlockSize];
        SuspendingValueParameterKernels.LaunchTupleSwapFieldsKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            // X = 1000, Y = t after the swap
            Assert.Equal(1000.0f * 100.0f + t, output[t]);
        }
    }

    internal static void AssertTupleSwapScalars()
    {
        var output = new int[BlockSize];
        SuspendingValueParameterKernels.LaunchTupleSwapScalarsKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(7 * 1000 + t, output[t]);
        }
    }

    internal static void AssertStructFieldWrite()
    {
        var fieldOut = new float[BlockSize];
        var scalarOut = new int[BlockSize];
        SuspendingValueParameterKernels.LaunchStructFieldWriteKernel(fieldOut, scalarOut, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 1.0f, fieldOut[t]);
            Assert.Equal(t + 1, scalarOut[t]);
        }
    }

    internal static void AssertNestedFieldWrite()
    {
        var output = new int[BlockSize];
        SuspendingValueParameterKernels.LaunchNestedFieldWriteKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 10 + 1000, output[t]);
        }
    }

    internal static void AssertInlineArrayElementWrite()
    {
        var output = new int[BlockSize];
        SuspendingValueParameterKernels.LaunchInlineArrayElementWriteKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 100 + 10000, output[t]);
        }
    }

    internal static void AssertPrefixIncrementField()
    {
        var output = new float[BlockSize];
        SuspendingValueParameterKernels.LaunchPrefixIncrementFieldKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 1.0f, output[t]);
        }
    }

    internal static void AssertRefFieldArgument()
    {
        var output = new float[BlockSize];
        SuspendingValueParameterKernels.LaunchRefFieldArgumentKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 2 + 1.0f, output[t]);
        }
    }

    internal static void AssertArrayElementWrite()
    {
        var values = new int[BlockSize];
        var output = new int[BlockSize];
        for (int t = 0; t < BlockSize; t++)
        {
            values[t] = t;
        }

        SuspendingValueParameterKernels.LaunchArrayElementWriteKernel(values, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t + 1000, output[t]);
            Assert.Equal(t + 1000, values[t]);
        }
    }
}
