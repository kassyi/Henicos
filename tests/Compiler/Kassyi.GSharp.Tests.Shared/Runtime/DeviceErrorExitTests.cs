using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Differential;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// A CUDA thread that fails a device check records the error and exits: the threads that do not fail run to the end,
/// also through block barriers, the launch throws the recorded exception, and the context stays usable. An mbarrier
/// waiter leaves the wait when a thread of its launch failed. The grid barrier has its own tests
/// (<see cref="GridBarrierTests"/>).
/// </summary>
/// <remarks>
/// The managed and native CPU paths stop the failing thread in the same way (Tests.Cpu); the mbarrier and the missing
/// error records are CUDA only.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public partial class DeviceErrorExitTests
{
    private const int Untouched = -7;
    private const int BlockSize = 128;
    private const int Rounds = 64;

    // The sum each surviving thread of BarrierLoopAfterFailKernel adds up: shared[0] = round + 1 in every round
    private const int BarrierLoopSum = Rounds * (Rounds + 1) / 2;

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DeviceError_NextLaunchInSameContext_Succeeds()
    {
        RequireDevice();
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            path.Run(() =>
            {
                int[] failing = [0, DeviceErrorExitKernels.FailKey, 1, 0];
                int[] result = Filled(failing.Length);

                var ex = Assert.Throws<SwitchExpressionException>(() =>
                    DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(failing, result,
                        blockDimX: (uint)failing.Length));
                AssertFailingThread(path, ex, "(1,0,0)", "(0,0,0)");
                Assert.Null(ex.InnerException);
                Assert.Equal([0, Untouched, -2, 3], result);

                int[] keys = [0, 1, 0, 1];
                int[] next = Filled(keys.Length);
                DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(keys, next, blockDimX: (uint)keys.Length);
                Assert.Equal([0, -1, 2, -3], next);
            });
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void BarrierLoop_PartOfEveryWarpFails_RemainingThreadsCompleteLoop()
    {
        RequireDevice();
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
        RequireDevice();
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
        RequireDevice();
        const int Blocks = 8;
        const int FailingThread = 5 * BlockSize + 77;
        var keys = new int[Blocks * BlockSize];
        keys[FailingThread] = DeviceErrorExitKernels.FailKey;
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            path.Run(() =>
            {
                int[] result = Filled(keys.Length);

                var ex = Assert.Throws<SwitchExpressionException>(() =>
                    DeviceErrorExitKernels.LaunchWriteUnlessFailKernel(keys, result, gridDimX: Blocks,
                        blockDimX: BlockSize));
                AssertFailingThread(path, ex, "(77,0,0)", "(5,0,0)");

                for (int i = 0; i < result.Length; i++)
                {
                    Assert.Equal(i == FailingThread ? Untouched : i, result[i]);
                }
            });
        }
    }

    // Runs the barrier loop on each path of this build
    private static void AssertBarrierLoop(int[] keys)
    {
        foreach (var path in DiffPaths.Available)
        {
            path.Preflight?.Invoke();
            path.Run(() =>
            {
                int[] result = Filled(keys.Length);

                var ex = Assert.Throws<SwitchExpressionException>(() =>
                    DeviceErrorExitKernels.LaunchBarrierLoopAfterFailKernel(keys, Rounds, result,
                        blockDimX: BlockSize));
                Assert.Null(ex.InnerException);

                for (int i = 0; i < keys.Length; i++)
                {
                    Assert.Equal(keys[i] == DeviceErrorExitKernels.FailKey ? Untouched : BarrierLoopSum, result[i]);
                }
            });
        }
    }

    // The coordinates of the failing thread that the launch adds to the exception. The managed path throws the
    // exception of the C# check itself, which carries none
    private static void AssertFailingThread(DiffPathRunner path, Exception exception, string thread, string block)
    {
        if (path.Path == DiffPath.Managed)
        {
            return;
        }

        Assert.Equal(thread, exception.Data["GSharp.ThreadIndex"]);
        Assert.Equal(block, exception.Data["GSharp.BlockIndex"]);
    }

    // On CUDA the tests need the driver; the CPU paths need nothing
    private static void RequireDevice()
    {
        if (AssemblyDialect.Current.IsCuda)
        {
            CudaCapability.EnsureGpuAvailable();
        }
    }

    private static int[] Filled(int length)
    {
        var values = new int[length];
        Array.Fill(values, Untouched);
        return values;
    }
}
