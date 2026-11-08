using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The time a waiter at the grid barrier keeps waiting after it saw a recorded error (100 ms without progress): a block
/// that is only late arrives within it, so the launch throws the recorded exception rather than trapping.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
// A device error goes to the one error slot of the process, which a test running beside this one could read first
[Collection(TestCollections.DeviceExclusive)]
public sealed class GridBarrierTimingTests
{
    private const int Untouched = -7;
    private const int Blocks = 4;
    private const int BlockSize = 128;

    // CU_DEVICE_ATTRIBUTE_CLOCK_RATE, in kHz
    private const int ClockRateAttribute = 13;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SlowBlockAfterFailure_ArrivesWithinTheWaitAfterTheError()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        // Thread 5 of block 0 fails at once; block 3 spends about 40 ms (at the base clock) before the barrier. The
        // waiters see the error early but keep waiting up to 100 ms without progress, so the barrier completes.
        long cycles = 40L * ClockRateKilohertz();
        var keys = new int[Blocks * BlockSize];
        keys[5] = GridBarrierTimingKernels.FailKey;
        var result = new int[keys.Length];
        Array.Fill(result, Untouched);

        var ex = Assert.Throws<SwitchExpressionException>(() =>
            GridBarrierTimingKernels.LaunchSlowBlockAfterFailKernel(keys, slowBlock: 3, cycles, result,
                gridDimX: Blocks, blockDimX: BlockSize));

        Assert.Null(ex.InnerException);
        Assert.Equal(Untouched, result[5]);
        Assert.Equal(3 * BlockSize + 7, result[3 * BlockSize + 7]);
    }

    private static long ClockRateKilohertz()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(
            CudaDriverApi.cuDeviceGetAttribute(out var rate, (CudaDriverApi.CuDeviceAttribute)ClockRateAttribute, device)
        );
        return rate;
    }
}
