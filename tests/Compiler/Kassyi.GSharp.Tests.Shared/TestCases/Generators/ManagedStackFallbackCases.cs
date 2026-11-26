using System;
using Kassyi.GSharp.Core;

namespace ManagedStackFallbackGeneratorCases;

file struct StepValue
{
    public int V;
}

public struct Guard : IDisposable
{
    public int V;

    public void Dispose() => V = 0;
}

public struct WarpValue
{
    public int V;

    [GpuOperatorMacro(typeof(FallbackKernels), nameof(FallbackKernels.AddShuffled))]
    public static WarpValue operator +(WarpValue left, WarpValue right) => throw new GpuOnlyException();
}

// A kernel that the stack eligibility keeps on the state machine: it passes BarrierStep, which has no stack form, as a
// method group. BarrierStep waits at a block barrier and reaches no warp collective; its body names a file-local type,
// which the managed copies cannot name (GSH0119), so IsStackKernel rejects the kernel cleanly without running TryGenerate
[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class FallbackKernels
{
    [GpuKernel]
    public static void PassesSuspendingGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, BarrierStep);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void BarrierStep(int i)
    {
        var value = new StepValue { V = i };
        Gpu.SyncThreads();
        _ = value.V;
    }

    // A chain kernel -> Outer -> Middle -> Inner where only Inner's stack form cannot be made (its operator + calls a
    // device function that reaches a warp collective). Middle loses its form in the first pass over the device
    // functions; Outer, declared before Middle, still saw Middle's form in that pass and loses its own only in the
    // second, and then the kernel
    [GpuKernel]
    public static void CallsChainKernel(int[] data)
    {
        Outer(data);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void Outer(int[] data)
    {
        Gpu.SyncThreads();
        Middle(data);
    }

    [GpuDeviceFunction]
    private static void Middle(int[] data)
    {
        Gpu.SyncThreads();
        Inner(data);
    }

    [GpuDeviceFunction]
    private static void Inner(int[] data)
    {
        Gpu.SyncThreads();
        WarpValue x;
        x.V = data[ThreadIndex.X];
        x = x + x;
        data[ThreadIndex.X] = x.V + 2;
    }

    // A using declaration after a continue that leaves the region of the loop body, in a kernel that reaches
    // Warp.ActiveMask(): the goto of the region would pass the declaration (CS8648), so the kernel keeps its state
    // machine with the reason
    [GpuKernel]
    public static void UsingAfterContinueKernel(int[] data)
    {
        for (int i = 0; i < 2; i++)
        {
            if (ThreadIndex.X % 2 == 0)
            {
                continue;
            }

            using var guard = new Guard { V = i };
            data[ThreadIndex.X] += (int)Warp.ActiveMask() + guard.V;
        }
    }

    [GpuDeviceFunction]
    public static void AddShuffled(ref WarpValue result, WarpValue left, WarpValue right) =>
        result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
}
