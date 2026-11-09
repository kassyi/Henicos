// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The left operand of a binary expression whose right operand splits the state machine (a warp shuffle, a broadcast
/// helper that suspends) runs before the split, as C# evaluates it: the split reads what the left operand wrote.
/// </summary>
public static partial class EvaluationOrderSplitKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // C#: each lane writes a[lane] = lane + 100, then reads lane 0's a[0] (100) through the shuffle
    [GpuKernel]
    public static void AssignmentBeforeShuffleKernel(int[] a, int[] result)
    {
        int lane = ThreadIndex.X;
        result[lane] = (a[lane] = lane + 100) + Warp.ShuffleSync(FullMask, a[lane], 0);
    }

    // The same through a local: x is written by the left operand and read by the shuffle
    [GpuKernel]
    public static void LocalAssignmentBeforeShuffleKernel(int[] result)
    {
        int lane = ThreadIndex.X;
        int x = 0;
        result[lane] = (x = lane + 100) + Warp.ShuffleSync(FullMask, x, 0);
    }

    // A call with a side effect on the left: Bump runs before the shuffle reads a[lane]
    [GpuKernel]
    public static void CallBeforeShuffleKernel(int[] a, int[] result)
    {
        int lane = ThreadIndex.X;
        result[lane] = Bump(a, lane) + Warp.ShuffleSync(FullMask, a[lane], 0);
    }

    // A broadcast helper that suspends (a barrier inside) on the right
    [GpuKernel]
    public static void AssignmentBeforeSuspendingCallKernel(int[] a, int[] result)
    {
        int lane = ThreadIndex.X;
        result[lane] = (a[lane] = lane + 100) + ReadAfterBarrier(a, 0);
    }

    [GpuDeviceFunction]
    public static int Bump(int[] a, int lane)
    {
        a[lane] += 7;
        return 1;
    }

    [GpuDeviceFunction]
    public static int ReadAfterBarrier(int[] a, int index)
    {
        Gpu.SyncThreads();
        return a[index];
    }
}
