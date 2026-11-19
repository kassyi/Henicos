using Kassyi.GSharp.Core;

namespace ManagedStackFallbackGeneratorCases;

// A kernel that the stack model takes (a block barrier, no call of a function without a stack form) but whose stack form
// cannot be made: it names ShuffleStep, which reaches a warp shuffle and has no stack form, as a method group
[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class FallbackKernels
{
    [GpuKernel]
    public static void PassesSuspendingGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, ShuffleStep);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void ShuffleStep(int i)
    {
        Gpu.SyncThreads();
        _ = Warp.ShuffleXorSync(0xFFFFFFFFu, i, 1);
    }

    // A chain kernel -> Outer -> Middle -> Inner where only Inner's stack form cannot be made (it names ShuffleStep as
    // a method group). Middle loses its form in the first pass over the device functions; Outer, declared before
    // Middle, still saw Middle's form in that pass and loses its own only in the second, and then the kernel
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
        Gpu.ForUnroll(0, 2, ShuffleStep);
        data[ThreadIndex.X] += 2;
    }
}
