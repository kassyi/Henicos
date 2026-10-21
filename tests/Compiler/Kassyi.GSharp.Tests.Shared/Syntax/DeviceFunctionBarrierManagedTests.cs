#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback runs the same lowered state machine, so device-function barriers must suspend there too.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class DeviceFunctionBarrierManagedTests : TranspilerTestBase
{
    private const int Length = 64;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SharedMemoryExchangeInDeviceFunction_ReversesBlock()
    {
        var input = CreateSequence(Length);
        var output = new int[Length];

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchSharedMemoryReverseKernel(input, output, Length, blockDimX: Length));

        AssertReversed(input, output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_WarpShuffleScanInDeviceFunction_ComputesInclusivePrefixPerWarp()
    {
        var input = CreateSequence(Length);
        var output = new int[Length];

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchWarpInclusiveScanKernel(input, output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            int expected = 0;
            for (int j = i - i % 32; j <= i; j++)
            {
                expected += input[j];
            }

            Assert.Equal(expected, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_DeviceFunctionCallingBarrierDeviceFunction_KeepsLocalsAcrossInnerCall()
    {
        const int scale = 1000;
        var input = CreateSequence(Length);
        var output = new int[Length];

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchNestedBarrierCallKernel(input, output, Length, scale, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(input[Length - 1 - i] + i * scale, output[i]);
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(67)]
    public void Test_Managed_BarrierDeviceFunctionCalledInLoop_RestartsOnEveryIteration(int rounds)
    {
        var input = CreateSequence(Length);
        var data = (int[])input.Clone();

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchRepeatedRotateKernel(data, Length, rounds, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(input[(i + rounds) % Length], data[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierDeviceFunctionArgument_IsEvaluatedOnceAcrossResumes()
    {
        var input = CreateSequence(Length);
        var output = new int[Length];
        var tickets = new int[Length];

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchArgumentEvaluatedOnceKernel(input, output, tickets, Length, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(1, tickets[i]);
            Assert.Equal(input[Length - 1 - i] + 1, output[i]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_OutParametersOfBarrierDeviceFunction_ReachTheCaller()
    {
        var input = CreateSequence(Length);
        var reversed = new int[Length];
        var original = new int[Length];

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchOutParameterKernel(input, reversed, original, Length, blockDimX: Length));

        AssertReversed(input, reversed);
        Assert.Equal(input, original);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_BarrierDeviceFunctionWithSpanParameter_ReversesSelectedSpan()
    {
        var primary = CreateSequence(32);
        var alternate = CreateSequence(32);
        var primaryInput = (int[])primary.Clone();
        var alternateInput = (int[])alternate.Clone();

        RunManaged(() => DeviceFunctionBarrierKernels.LaunchUniformBranchSpanKernel(primary, alternate, 1, blockDimX: 64));

        AssertReversed(primaryInput, primary);
        Assert.Equal(alternateInput, alternate);
    }

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }

    private static int[] CreateSequence(int length)
    {
        var values = new int[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = i * 7 + 3;
        }

        return values;
    }

    private static void AssertReversed(int[] input, int[] output)
    {
        int length = input.Length;
        for (int i = 0; i < length; i++)
        {
            Assert.Equal(input[length - 1 - i], output[i]);
        }
    }
}
#endif
