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

    // Traps when the current 3D thread and block coordinates match target
    [GpuKernel]
    public static void Coordinate3DTrapKernel(
        int[] data, TrapTargetCoordinates target, int[] result)
    {
        int targetTx = target.ThreadX;
        int targetTy = target.ThreadY;
        int targetTz = target.ThreadZ;
        int targetBx = target.BlockX;
        int targetBy = target.BlockY;
        int targetBz = target.BlockZ;
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

    [GpuKernel]
    public static void BlockIndex100TrapKernel(int[] data, int targetBlock, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == targetBlock)
        {
            result[0] = data.AsStackEnumerable().ElementAt(999);
        }
    }

    [GpuKernel]
    public static void StackLinqFirstEmptyKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = (data.AsStackEnumerable().Take(0)).First();
        }
    }

    [GpuKernel]
    public static void StackLinqSingleMultipleKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = (data.AsStackEnumerable().Take(2)).Single();
        }
    }

    [GpuKernel]
    public static void StackLinqMaxEmptyKernel(int[] data, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = (data.AsStackEnumerable().Take(0)).Max();
        }
    }

#pragma warning disable CS8509 // The switch expressions below leave inputs unmatched on purpose
    [GpuKernel]
    public static void SwitchExpressionNoMatchKernel(int[] keys, int[] result)
    {
        int i = ThreadIndex.X;
        result[i] = keys[i] switch { 0 => 10, 1 => 20 };
    }

    [GpuKernel]
    public static void SwitchExpressionNoMatchInDeviceFunctionKernel(int[] keys, int[] result)
    {
        int i = ThreadIndex.X;
        result[i] = ArmValue(keys[i]);
    }

    [GpuDeviceFunction]
    public static int ArmValue(int key) => key switch { 0 => 10, 1 => 20 };
#pragma warning restore CS8509
}

public struct TrapTargetCoordinates
{
    public int ThreadX;
    public int ThreadY;
    public int ThreadZ;
    public int BlockX;
    public int BlockY;
    public int BlockZ;
}
