#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Cpu.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public class NativeStackTests
{
    private const int Sentinel = -77;

    private static readonly string s_dllPath = DiffPaths.NativeLibraryPath(typeof(NativeStackTests).Assembly);

    private static bool IsStackBuild() =>
        typeof(NativeStackTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(static a => string.Equals(a.Key, "GSharpNativeBarrierModel", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(a.Value, "Stack", StringComparison.OrdinalIgnoreCase));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NeighbourKernel_FourThreads_ReadsNeighbourAfterBarrier()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var buffer = new int[4];
        var output = new int[4];

        NativeStackKernels.LaunchNeighbourKernel(buffer, output, blockDimX: 4);

        Assert.Equal([2, 3, 4, 1], output);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MultiBlockNeighbourKernel_256BlocksBy4Threads_ReusesStacksAcrossBlocks()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        const int Blocks = 256;
        const int Threads = 4;
        var buffer = new int[Blocks * Threads];
        var output = new int[Blocks * Threads];

        NativeStackKernels.LaunchMultiBlockNeighbourKernel(buffer, output, gridDimX: Blocks, blockDimX: Threads);

        for (int b = 0; b < Blocks; b++)
        {
            for (int t = 0; t < Threads; t++)
            {
                int expected = (b + 1) * 10000 + ((t + 1) % Threads) + 1;
                Assert.Equal(expected, output[b * Threads + t]);
            }
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LoopBarrierLocalsKernel_HundredIterations_PreservesLocalsAcrossBarriers()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        const int Threads = 8;
        var scratch = new int[Threads];
        var output = new int[Threads];

        NativeStackKernels.LaunchLoopBarrierLocalsKernel(scratch, output, blockDimX: Threads);

        for (int t = 0; t < Threads; t++)
        {
            int expected = (t + 1) + 100 * (t * 7 + ((t + 1) % Threads)) + (100 * 99) / 2;
            Assert.Equal(expected, output[t]);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EarlyReturnBeforeBarrierKernel_SomeThreadsReturn_BarrierReleasesActiveThreads()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        const int Threads = 6;
        const int ActiveCount = 4;
        var buffer = new int[Threads];
        var output = new int[Threads];

        NativeStackKernels.LaunchEarlyReturnBeforeBarrierKernel(buffer, output, ActiveCount, blockDimX: Threads);

        Assert.Equal([11, 12, 13, 10, -1, -1], output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReturningBarrierKernel_FourThreads_ReturnsPerThreadValues()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var buffer = new int[4];

        var result = NativeStackKernels.LaunchReturningBarrierKernel(buffer, blockDimX: 4);

        Assert.Equal([11, 13, 15, 13], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryBarrierKernel_EightThreads_SynchronizesSharedArena()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        int[] input = [1, 2, 3, 4, 5, 6, 7, 8];
        var output = new int[8];

        NativeStackKernels.LaunchSharedMemoryBarrierKernel(input, output, blockDimX: 8);

        Assert.Equal([8, 10, 12, 14, 16, 2, 4, 6], output);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FailureAfterBarrierKernel_Thread2OutOfRange_ThrowsAndOtherThreadsComplete()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        int[] source = [50, 60];
        var before = Enumerable.Repeat(Sentinel, 4).ToArray();
        var after = Enumerable.Repeat(Sentinel, 4).ToArray();

        var exception = Assert.Throws<IndexOutOfRangeException>(() =>
            NativeStackKernels.LaunchFailureAfterBarrierKernel(source, before, after,
                failingThread: 2, badIndex: 5, blockDimX: 4));

        Assert.Equal("(2,0,0)", exception.Data["GSharp.ThreadIndex"]);
        Assert.Equal([100, 101, 102, 103], before);
        Assert.Equal([50, 51, Sentinel, 53], after);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TwoThreadsFailAfterBarrierKernel_Threads1And3Fail_ReportsThread1First()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        int[] source = [50, 60];
        var after = Enumerable.Repeat(Sentinel, 4).ToArray();

        var exception = Assert.Throws<IndexOutOfRangeException>(() =>
            NativeStackKernels.LaunchTwoThreadsFailAfterBarrierKernel(source, after, badIndex: 9, blockDimX: 4));

        Assert.Equal("(1,0,0)", exception.Data["GSharp.ThreadIndex"]);
        Assert.Equal([50, Sentinel, 52, Sentinel], after);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void UsingUnwindAfterBarrierKernel_FailsInsideUsing_RunsDispose()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        int[] source = [50];
        var log = Enumerable.Repeat(Sentinel, 7).ToArray();

        Assert.Throws<IndexOutOfRangeException>(() =>
            NativeStackKernels.LaunchUsingUnwindAfterBarrierKernel(source, log, badIndex: 3, blockDimX: 4));

        Assert.Equal([1, 1, 1, 1, 777, Sentinel, Sentinel], log);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FailureAfterBarrierKernel_ValidLaunchAfterFailure_SucceedsWithoutStaleState()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        int[] source = [50, 60];
        var before = Enumerable.Repeat(Sentinel, 4).ToArray();
        var after = Enumerable.Repeat(Sentinel, 4).ToArray();

        Assert.Throws<IndexOutOfRangeException>(() =>
            NativeStackKernels.LaunchFailureAfterBarrierKernel(source, before, after,
                failingThread: 2, badIndex: 5, blockDimX: 4));

        before = Enumerable.Repeat(Sentinel, 4).ToArray();
        after = Enumerable.Repeat(Sentinel, 4).ToArray();
        NativeStackKernels.LaunchFailureAfterBarrierKernel(source, before, after,
            failingThread: 2, badIndex: 1, blockDimX: 4);

        Assert.Equal([100, 101, 102, 103], before);
        Assert.Equal([50, 51, 62, 53], after);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void NeighbourKernelSource_StackModelBuild_UsesStackResumeWithoutStateMachineYield()
    {
        Skip.IfNot(IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        Assert.Contains(
            GSharpInternalNames.NativeStack.Resume,
            NativeStackKernels.NeighbourKernel_Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__gsharp_yielded_for_block_barrier=true",
            NativeStackKernels.NeighbourKernel_Source,
            StringComparison.Ordinal);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void NeighbourKernelSource_DefaultStateMachineBuild_UsesStateMachineWithoutStackResume()
    {
        Skip.If(IsStackBuild(), "Runs only when GSharpNativeBarrierModel is not Stack.");

        Assert.DoesNotContain(
            GSharpInternalNames.NativeStack.Resume,
            NativeStackKernels.NeighbourKernel_Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_yielded_for_block_barrier=true",
            NativeStackKernels.NeighbourKernel_Source,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OneLevelSuspendingDeviceKernel_FourThreads_PreservesCallerLocalAndExchangesAcrossBarrier()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var scratch = new int[4];
        var output = new int[4];

        NativeStackKernels.LaunchOneLevelSuspendingDeviceKernel(scratch, output, blockDimX: 4);

        Assert.Equal([2, 13, 24, 31], output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MultiLevelSuspendingDeviceKernel_FourThreads_ExchangesAcrossNestedSuspendingCalls()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var scratch = new int[4];
        var output = new int[4];

        NativeStackKernels.LaunchMultiLevelSuspendingDeviceKernel(scratch, output, blockDimX: 4);

        Assert.Equal([104, 101, 102, 103], output);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingDeviceUsingKernel_FourThreads_DisposesStructWithoutDuplicateDefinitions()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var log = Enumerable.Repeat(Sentinel, 6).ToArray();

        NativeStackKernels.LaunchSuspendingDeviceUsingKernel(log, marker: 555, blockDimX: 4);

        Assert.Equal([1, 1, 1, 1, 555, 556], log);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ShuffleDeviceFallbackKernel_FourThreads_ExecutesViaStateMachineFallback()
    {
        DiffPaths.EnsureNativeLibrary(s_dllPath);
        var output = new int[4];

        NativeStackKernels.LaunchShuffleDeviceFallbackKernel(output, blockDimX: 4);

        Assert.Equal([11, 10, 13, 12], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingDeviceKernelSources_StackModelBuild_RunEligibleOnStackAndKeepShuffleFallbackOnStateMachine()
    {
        Skip.IfNot(IsStackBuild(), "Runs only when GSharpNativeBarrierModel=Stack.");

        Assert.Contains(
            GSharpInternalNames.NativeStack.Resume,
            NativeStackKernels.OneLevelSuspendingDeviceKernel_Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__gsharp_yielded_for_block_barrier=true",
            NativeStackKernels.OneLevelSuspendingDeviceKernel_Source,
            StringComparison.Ordinal);
        Assert.Contains(
            GSharpInternalNames.NativeStack.Resume,
            NativeStackKernels.MultiLevelSuspendingDeviceKernel_Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            GSharpInternalNames.NativeStack.Resume,
            NativeStackKernels.ShuffleDeviceFallbackKernel_Source,
            StringComparison.Ordinal);
    }
}
