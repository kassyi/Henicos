using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Barriers inside device functions must suspend every thread on the CPU dialect exactly as they do on CUDA.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class DeviceFunctionBarrierTests : TranspilerTestBase
{
    private const int WarpSize = 32;

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public void Test_SharedMemoryExchangeInDeviceFunction_ReversesBlock(int length)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchSharedMemoryReverseKernel(input, output, length, blockDimX: (uint)length);

        AssertReversed(input, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(33)]
    public void Test_SharedMemoryExchangeInDeviceFunction_ReversesPartialWarps(int length)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchSharedMemoryReverseKernel(input, output, length, blockDimX: (uint)length);

        AssertReversed(input, output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public void Test_WarpShuffleScanInDeviceFunction_ComputesInclusivePrefixPerWarp(int length)
    {
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchWarpInclusiveScanKernel(input, output, blockDimX: (uint)length);

        Assert.Equal(ExpectedWarpInclusiveScan(input), output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    public void Test_WarpShuffleScanInKernelBody_ComputesInclusivePrefixPerWarp(int length)
    {
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchWarpInclusiveScanInKernelBodyKernel(input, output, blockDimX: (uint)length);

        Assert.Equal(ExpectedWarpInclusiveScan(input), output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DeviceFunctionCallingBarrierDeviceFunction_KeepsLocalsAcrossInnerCall()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int length = 64;
        const int scale = 1000;
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchNestedBarrierCallKernel(input, output, length, scale, blockDimX: length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(input[length - 1 - i] + i * scale, output[i]);
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    [InlineData(67)]
    public void Test_BarrierDeviceFunctionCalledInLoop_RestartsOnEveryIteration(int rounds)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int length = 64;
        var input = CreateSequence(length);
        var data = (int[])input.Clone();

        DeviceFunctionBarrierKernels.LaunchRepeatedRotateKernel(data, length, rounds, blockDimX: length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(input[(i + rounds) % length], data[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BarrierDeviceFunctionArgument_IsEvaluatedOnceAcrossResumes()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int length = 64;
        var input = CreateSequence(length);
        var output = new int[length];
        var tickets = new int[length];

        DeviceFunctionBarrierKernels.LaunchArgumentEvaluatedOnceKernel(input, output, tickets, length, blockDimX: length);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(1, tickets[i]);
            Assert.Equal(input[length - 1 - i] + 1, output[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SharedMemoryPassedToBarrierDeviceFunction_IsSharedByTheBlock()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int length = 64;
        var input = CreateSequence(length);
        var output = new int[length];

        DeviceFunctionBarrierKernels.LaunchSharedMemoryParameterKernel(input, output, length, blockDimX: length);

        AssertReversed(input, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OutParametersOfBarrierDeviceFunction_ReachTheCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        const int length = 64;
        var input = CreateSequence(length);
        var reversed = new int[length];
        var original = new int[length];

        DeviceFunctionBarrierKernels.LaunchOutParameterKernel(input, reversed, original, length, blockDimX: length);

        AssertReversed(input, reversed);
        Assert.Equal(input, original);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(0)]
    public void Test_BarrierDeviceFunctionInUniformBranchAfterEarlyReturn_ReversesSelectedSpan(int usePrimary)
    {
        // 64 threads: the second warp returns before the call, as the stdlib warp kernels do
        var primary = CreateSequence(WarpSize);
        var alternate = CreateSequence(WarpSize);
        var primaryInput = (int[])primary.Clone();
        var alternateInput = (int[])alternate.Clone();

        DeviceFunctionBarrierKernels.LaunchUniformBranchSpanKernel(primary, alternate, usePrimary, blockDimX: 2 * WarpSize);

        var (changed, changedInput, unchanged, unchangedInput) = usePrimary != 0
            ? (primary, primaryInput, alternate, alternateInput)
            : (alternate, alternateInput, primary, primaryInput);
        AssertReversed(changedInput, changed);
        Assert.Equal(unchangedInput, unchanged);
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

    private static int[] ExpectedWarpInclusiveScan(int[] input)
    {
        var expected = new int[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            int sum = input[i];
            if (i % WarpSize != 0)
            {
                sum += expected[i - 1];
            }

            expected[i] = sum;
        }

        return expected;
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
