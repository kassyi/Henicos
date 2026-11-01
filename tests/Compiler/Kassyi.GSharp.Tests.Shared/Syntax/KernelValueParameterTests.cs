using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution tests for writes into a value parameter of a kernel before a barrier (#879): the value written before the
/// barrier must be the one read after it on every dialect.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class KernelValueParameterTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ScalarParameterAssignment_SurvivesBarrier() => AssertScalarParameter();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ArrayParameterAssignment_SurvivesBarrier() => AssertArrayParameter();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ReadOnlyArrayParameterAssignment_SurvivesBarrier() => AssertReadOnlyArrayParameter();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArrayParameterAssignedFromStructField_SurvivesBarrier() => AssertArrayFromStructField();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StructParameterAssignment_SurvivesBarrier() => AssertStructParameter();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StructParameterFieldWrite_SurvivesBarrier() => AssertStructFieldParameter();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ParameterWrittenAcrossTwoBarriers_KeepsEveryWrite() => AssertAcrossTwoBarriers();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ParameterWrittenInBarrierLoop_KeepsEveryIteration() => AssertInBarrierLoop();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefExtensionReceiver_SurvivesBarrier() => AssertRefExtensionReceiver();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefArgumentAndIncrement_SurvivesBarrier() => AssertRefArgumentAndIncrement();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OutArgument_SurvivesBarrier() => AssertOutArgument();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Deconstruction_SurvivesBarrier() => AssertDeconstruction();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LocalFunctionWrite_SurvivesBarrier() => AssertLocalFunctionWrite();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ParameterWrittenWithoutBarrier_KeepsWrite() => AssertWithoutBarrier();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefTernaryParameterWrite_SurvivesBarrier() => AssertRefTernaryParameter();

    internal static void AssertScalarParameter()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchScalarParameterKernel(7, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(7 + t, output[t]);
        }
    }

    internal static void AssertArrayParameter()
    {
        // The original argument is longer and holds other values, so reading it after the barrier fails both checks
        var values = CreateFilled(BlockSize + 3, 900);
        var replacement = CreateFilled(BlockSize, 1);
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchArrayParameterKernel(values, replacement, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(BlockSize * 1000 + 1 + t, output[t]);
        }
    }

    internal static void AssertReadOnlyArrayParameter()
    {
        var values = CreateFilled(BlockSize + 3, 900);
        var replacement = CreateFilled(BlockSize, 1);
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchReadOnlyArrayParameterKernel(values, replacement, output,
            blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(BlockSize * 1000 + 1 + t, output[t]);
        }
    }

    internal static void AssertArrayFromStructField()
    {
        var holder = new KernelParameterHolder { Data = CreateFilled(BlockSize, 1) };
        var values = CreateFilled(BlockSize + 3, 900);
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchArrayFromStructFieldKernel(holder, values, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(BlockSize * 1000 + 1 + t, output[t]);
        }
    }

    internal static void AssertStructParameter()
    {
        var point = new KernelParameterPoint { X = 1, Y = 2 };
        var replacement = new KernelParameterPoint { X = 5, Y = 100 };
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchStructParameterKernel(point, replacement, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(5 * 1000 + 100 + t, output[t]);
        }
    }

    internal static void AssertStructFieldParameter()
    {
        var point = new KernelParameterPoint { X = 3, Y = 9 };
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchStructFieldParameterKernel(point, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal((3 + t) * 1000 + 9, output[t]);
        }
    }

    internal static void AssertAcrossTwoBarriers()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchParameterAcrossTwoBarriersKernel(4, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal((4 + t) * 10, output[t]);
        }
    }

    internal static void AssertInBarrierLoop()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchParameterInBarrierLoopKernel(1, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            // 1 -> 2 -> 5 -> 12
            Assert.Equal(12 + t * 100, output[t]);
        }
    }

    internal static void AssertRefExtensionReceiver()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchRefExtensionReceiverKernel(new KernelParameterPoint { X = 7 }, output,
            blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(7 + t, output[t]);
        }
    }

    internal static void AssertRefArgumentAndIncrement()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchRefArgumentAndIncrementKernel(7, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(7 + t + 1, output[t]);
        }
    }

    internal static void AssertOutArgument()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchOutArgumentKernel(-5, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 3, output[t]);
        }
    }

    internal static void AssertDeconstruction()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchDeconstructionKernel(7, 3, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal((7 + t) * 1000 + 4, output[t]);
        }
    }

    internal static void AssertLocalFunctionWrite()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchLocalFunctionWriteKernel(20, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(20 + t, output[t]);
        }
    }

    internal static void AssertWithoutBarrier()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchParameterWithoutBarrierKernel(11, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(11 + t, output[t]);
        }
    }

    internal static void AssertRefTernaryParameter()
    {
        var output = new int[BlockSize];
        KernelValueParameterKernels.LaunchRefTernaryParameterKernel(10, 20, 3, output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            int expectedAssign = (t & 1) == 0 ? 777 : 10;
            int expectedCompound = (t & 2) == 0 ? 120 : 20;
            int expectedInc = (t & 4) == 0 ? 4 : 3;
            Assert.Equal(expectedAssign * 10000 + expectedCompound * 10 + expectedInc, output[t]);
        }
    }

    // first, first + 1, ...
    private static int[] CreateFilled(int length, int first)
    {
        var values = new int[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = first + i;
        }

        return values;
    }
}
