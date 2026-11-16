using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public partial class DeviceErrorExitTests
{
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
}
