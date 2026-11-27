using Kassyi.GSharp.Core;
using StackTraitsCase;

namespace CrossAssemblyStackConsumerCase;

[SkipSnapshotConsistency("Input for cross-assembly stack consumer tests")]
public static partial class StackConsumerKernels
{
    [GpuKernel]
    public static void CallsBlockBarrierKernel(int[] data)
    {
        data[ThreadIndex.X] = StackTraitsLib.WaitBlock(data, ThreadIndex.X);
    }

    [GpuKernel]
    public static void CallsGridBarrierKernel(int[] data)
    {
        StackTraitsLib.WaitGrid();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void CallsWarpShuffleKernel(int[] data)
    {
        data[ThreadIndex.X] = StackTraitsLib.WaitShuffle(data[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void CallsActiveMaskKernel(int[] data)
    {
        data[ThreadIndex.X] = (int)StackTraitsLib.WaitActiveMask();
    }

    [GpuKernel]
    public static void CallsNonPartialKernel(int[] data)
    {
        data[ThreadIndex.X] = ClosedStackLib.WaitClosed(data, ThreadIndex.X);
    }

    [GpuDeviceFunction]
    public static int CallsBlockBarrierDevice(int[] data, int tid) => StackTraitsLib.WaitBlock(data, tid);

    [GpuKernel]
    public static void CallsThroughLocalDeviceKernel(int[] data)
    {
        data[ThreadIndex.X] = CallsBlockBarrierDevice(data, ThreadIndex.X);
    }
}

[SkipSnapshotConsistency("Input for cross-assembly stack consumer tests")]
public partial class DerivedStackConsumer : BaseStackLib
{
    [GpuKernel]
    public static void CallsProtectedBarrierKernel(int[] data)
    {
        data[ThreadIndex.X] = WaitProtected(data, ThreadIndex.X);
    }
}
