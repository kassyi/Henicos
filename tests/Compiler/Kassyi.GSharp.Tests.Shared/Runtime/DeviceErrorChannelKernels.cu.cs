using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[SkipSnapshotConsistency]
public static partial class DeviceErrorChannelKernels
{
    [GpuKernel]
    public static void StackLinqFirstNoMatchKernel(int[] data, int threshold, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = data.AsStackEnumerable().First(x => x > threshold);
        }
    }

    [GpuKernel]
    public static void StackLinqElementAtKernel(int[] data, int index, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = data.AsStackEnumerable().ElementAt(index);
        }
    }

    [GpuKernel]
    public static void ConcurrentTrapAllThreadsKernel(int[] data, int[] result)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[tid] = data.AsStackEnumerable().First(x => x > 1000000);
    }

    [GpuKernel]
    public static void Coordinate3DTrapKernel(
        int[] data,
        int targetTx,
        int targetTy,
        int targetTz,
        int targetBx,
        int targetBy,
        int targetBz,
        int[] result)
    {
        if (ThreadIndex.X == targetTx &&
            ThreadIndex.Y == targetTy &&
            ThreadIndex.Z == targetTz &&
            BlockIndex.X == targetBx &&
            BlockIndex.Y == targetBy &&
            BlockIndex.Z == targetBz)
        {
            result[0] = data.AsStackEnumerable().ElementAt(999);
        }
    }
}
