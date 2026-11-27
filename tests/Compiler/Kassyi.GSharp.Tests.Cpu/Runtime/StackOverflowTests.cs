#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Kassyi.GSharp.Tests.Cpu.Core;
using Kassyi.GSharp.Tests.Shared.Differential;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class StackOverflowTests
{
    private const int Sentinel = -77;

    private static readonly string s_dllPath = DiffPaths.NativeLibraryPath(typeof(StackOverflowTests).Assembly);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LargeLocalAcrossBarrier_OnNativeStack_Runs()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");
        DiffPaths.EnsureNativeLibrary(s_dllPath);

        const uint blocks = 4;
        const uint threadsPerBlock = 32;
        var (input, expected) = BuildLargeLocalInputs(blocks, threadsPerBlock);
        var output = new int[(int)(blocks * threadsPerBlock)];

        StackModelKernels.LaunchNativeLargeLocalAcrossBarrierKernel(
            input, output, gridDimX: blocks, blockDimX: threadsPerBlock);

        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void HugeLocal_OnNativeStack_ReportsOverflowAndOtherThreadsFinish()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");
        DiffPaths.EnsureNativeLibrary(s_dllPath);

        AssertHugeLocalOverflow();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchAfterOverflow_OnNativeStack_Runs()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");
        DiffPaths.EnsureNativeLibrary(s_dllPath);

        AssertHugeLocalOverflow();

        const uint blocks = 4;
        const uint threadsPerBlock = 32;
        var (input, expected) = BuildLargeLocalInputs(blocks, threadsPerBlock);
        var output = new int[(int)(blocks * threadsPerBlock)];

        StackModelKernels.LaunchNativeLargeLocalAcrossBarrierKernel(
            input, output, gridDimX: blocks, blockDimX: threadsPerBlock);

        Assert.Equal(expected, output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedNullReferenceException_AfterNativeStackLaunch_OnCallingAndOtherThread_ThrowsNullReferenceException()
    {
        Skip.IfNot(NativeStackTests.IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");
        DiffPaths.EnsureNativeLibrary(s_dllPath);

        AssertHugeLocalOverflow();

        const uint blocks = 4;
        const uint threadsPerBlock = 32;
        var (input, expected) = BuildLargeLocalInputs(blocks, threadsPerBlock);
        var output = new int[(int)(blocks * threadsPerBlock)];
        StackModelKernels.LaunchNativeLargeLocalAcrossBarrierKernel(
            input, output, gridDimX: blocks, blockDimX: threadsPerBlock);
        Assert.Equal(expected, output);

        Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));

        Exception? otherThreadFailure = null;
        var otherThread = new Thread(() =>
        {
            try
            {
                Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));
            }
            catch (Exception ex)
            {
                otherThreadFailure = ex;
            }
        });
        otherThread.Start();
        otherThread.Join();
        Assert.Null(otherThreadFailure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DereferenceNullValues(int[] values) => values.Length;

    private struct NullArrayHolder
    {
        public int[] Values;
    }

    private static void AssertHugeLocalOverflow()
    {
        const uint blocks = 2;
        const uint threadsPerBlock = 4;
        var output = Enumerable.Repeat(Sentinel, (int)(blocks * threadsPerBlock)).ToArray();

        var exception = Assert.Throws<InsufficientExecutionStackException>(() =>
            StackModelKernels.LaunchHugeLocalOverflowKernel(output, gridDimX: blocks, blockDimX: threadsPerBlock));

        Assert.Contains("thread (1,0,0) of block (0,0,0)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("overflowed its stack of 1024 KiB", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GSharpNativeStackBytes", exception.Message, StringComparison.Ordinal);
        Assert.Equal("(1,0,0)", exception.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(0,0,0)", exception.Data["GSharp.BlockIndex"]);

        for (int i = 0; i < output.Length; i++)
        {
            Assert.Equal(i == 1 ? Sentinel : 1, output[i]);
        }
    }

    private static (int[] Input, int[] Expected) BuildLargeLocalInputs(uint blocks, uint threadsPerBlock)
    {
        int total = (int)(blocks * threadsPerBlock);
        var input = new int[total];
        var expected = new int[total];
        for (int i = 0; i < total; i++)
        {
            int v = (i * 37 + 11) & 0xFFFF;
            input[i] = v;
            int checksum = 0;
            for (int offset = 4096; offset < 300 * 1024 - 1; offset += 4096)
            {
                checksum += (byte)((v + (offset >> 12)) & 0xFF);
            }

            int mirrored = (int)threadsPerBlock - 1 - (i % (int)threadsPerBlock);
            expected[i] = (v & 0xFF) + ((v >> 8) & 0xFF) * 256 + checksum + mirrored;
        }

        return (input, expected);
    }
}
