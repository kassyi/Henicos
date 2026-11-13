using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// A CUDA thread that fails a device check records the error and exits: the threads that do not fail run to the end,
/// also through block barriers, the launch throws the recorded exception, and the context stays usable. An mbarrier
/// waiter leaves the wait when a thread of its launch failed. The grid barrier has its own tests
/// (<see cref="GridBarrierTests"/>).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public sealed class DeviceErrorExitTests
{
    private const int Untouched = -7;
    private const int BlockSize = 128;
    private const int Rounds = 64;

    // The sum each surviving thread of BarrierLoopAfterFailKernel adds up: shared[0] = round + 1 in every round
    private const int BarrierLoopSum = Rounds * (Rounds + 1) / 2;

#if GSHARP_CUDA
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceError_NextLaunchInSameContext_Succeeds()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        int[] failing = [0, DeviceErrorExitKernels.FailKey, 1, 0];
        int[] result = Filled(failing.Length);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(failing, result, blockDimX: (uint)failing.Length));
        Assert.Equal("(1,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Null(ex.InnerException);

        int[] keys = [0, 1, 0, 1];
        int[] next = Filled(keys.Length);
        DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(keys, next, blockDimX: (uint)keys.Length);
        Assert.Equal([0, -1, 2, -3], next);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void BarrierLoop_PartOfEveryWarpFails_RemainingThreadsCompleteLoop()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Every third thread fails, so each warp loses some of its lanes; thread 1 writes the shared value
        var keys = new int[BlockSize];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = i % 3 == 0 ? DeviceErrorExitKernels.FailKey : i % 2;
        }

        AssertBarrierLoop(keys);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void BarrierLoop_WholeWarpsFail_RemainingWarpsCompleteLoop()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Threads 32 to 95, two whole warps, fail
        var keys = new int[BlockSize];
        for (int i = 32; i < 96; i++)
        {
            keys[i] = DeviceErrorExitKernels.FailKey;
        }

        AssertBarrierLoop(keys);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceError_OneThreadFails_EveryOtherThreadWritesItsResult()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const int Blocks = 8;
        const int FailingThread = 5 * BlockSize + 77;
        var keys = new int[Blocks * BlockSize];
        keys[FailingThread] = DeviceErrorExitKernels.FailKey;
        int[] result = Filled(keys.Length);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(keys, result, gridDimX: Blocks, blockDimX: BlockSize));
        Assert.Equal("(77,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Equal("(5,0,0)", ex.Data["GSharp.BlockIndex"]);

        for (int i = 0; i < result.Length; i++)
        {
            Assert.Equal(i == FailingThread ? Untouched : i, result[i]);
        }
    }

    // [WHY] An mbarrier waits for a fixed number of arrivals, which a thread that exited never makes; the waiters read
    // the abort word of their launch, which the failing thread set, and exit too. The timeout fails the test instead of
    // waiting for a hang that the driver would end only by resetting the GPU.
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task MBarrierWait_ThreadFailsBeforeBarrier_WaitersLeaveAndLaunchThrows()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var keys = new int[32];
        keys[5] = DeviceErrorExitKernels.FailKey;
        int[] result = Filled(keys.Length);

        var launch = Task.Run(() => Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorExitKernels.LaunchMBarrierFailKernel(keys, result, blockDimX: (uint)keys.Length)));
        var ex = await launch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("(5,0,0)", ex.Data["GSharp.ThreadIndex"]);
        Assert.Null(ex.InnerException);
        // Every thread waits at the mbarrier before it writes, so none writes
        Assert.All(result, static value => Assert.Equal(Untouched, value));
    }

    // [WHY] Without the error records (the host could not allocate them) a failing thread has nowhere to record its
    // error, so it traps instead of exiting with no exception: a real GPU fault, run in a child process of its own and
    // only in `just test-gpu-trap`
    [SkippableFact]
    [Category(TestCategory.GpuTrap)]
    [Scenario(TestScenario.SadPath)]
    public unsafe void Trap_WithoutErrorRecords_FailingThreadTraps()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        if (!IsolatedTestProcess.IsChild(nameof(Trap_WithoutErrorRecords_FailingThreadTraps)))
        {
            CudaTrapRuns.RunIsolated(typeof(DeviceErrorExitTests), nameof(Trap_WithoutErrorRecords_FailingThreadTraps));
            return;
        }

        // The module's pointers to the records, as a failed allocation leaves them
        var module = Kassyi.GSharp.Generated.GSharpModuleContext_Kassyi_x00002eGSharp_x00002eTests_x00002eCuda
            .GetOrLoadModule();
        using (new GpuKernelLoader.CudaContextScope())
        {
            foreach (var name in (ReadOnlySpan<string>)["__gsharp_error_slot", "__gsharp_launch_errors"])
            {
                CudaException.ThrowIfError(CudaDriverApi.cuModuleGetGlobal_v2(out var global, out _, module, name));
                ulong none = 0;
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(global, (IntPtr)(&none), sizeof(ulong)));
            }
        }

        int[] keys = [0, DeviceErrorExitKernels.FailKey, 1, 0];
        int[] result = Filled(keys.Length);
        var cudaEx = Assert.Throws<CudaException>(() =>
            DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(keys, result, blockDimX: (uint)keys.Length));
        Assert.True(cudaEx.Result is CUresult.CudaErrorIllegalInstruction or CUresult.CudaErrorLaunchFailed);
    }

    private static void AssertBarrierLoop(int[] keys)
    {
        int[] result = Filled(keys.Length);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            DeviceErrorExitKernels.LaunchBarrierLoopAfterFailKernel(keys, Rounds, result, blockDimX: BlockSize));
        Assert.Null(ex.InnerException);

        for (int i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i] == DeviceErrorExitKernels.FailKey ? Untouched : BarrierLoopSum, result[i]);
        }
    }
#endif

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
