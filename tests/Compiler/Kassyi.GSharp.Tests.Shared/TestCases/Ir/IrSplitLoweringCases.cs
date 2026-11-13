using System;
using Kassyi.GSharp.Core;

namespace IrSplitLoweringCases;

public struct Cell
{
    public int F;
}

// The statement forms the split pass of the IR writes back as syntax; the split points are device functions that
// suspend (they reach a barrier) and a warp shuffle
public static class Cases
{
    [GpuDeviceFunction]
    public static int Suspend()
    {
        Gpu.SyncThreads();
        return 1;
    }

    [GpuDeviceFunction]
    public static int SuspendOut(out int y)
    {
        Gpu.SyncThreads();
        y = 5;
        return 1;
    }

    [GpuDeviceFunction]
    public static int Bump(int[] data, int i) => data[i] += 7;

    [GpuDeviceFunction]
    public static int Pair(int a, int b) => a - b;

    [GpuDeviceFunction]
    public static long TakeLong(long v, int s) => v + s;

    [GpuKernel]
    public static void EmbeddedStatement(bool c, int[] a, int[] result)
    {
        if (c)
            result[0] = a[0] + Suspend();
    }

    [GpuKernel]
    public static void ElseIfConditionSplit(int[] result)
    {
        if (result[0] > 0)
        {
            result[1] = 1;
        }
        else if (Suspend() > result[2])
        {
            result[1] = 2;
        }
    }

    [GpuDeviceFunction]
    public static int ExpressionBodied(int[] a) => a[0] + Suspend();

    [GpuKernel]
    public static void OutDeclarationOfASplit(int[] result)
    {
        result[0] = SuspendOut(out int y) + y;
    }

    [GpuKernel]
    public static void LaterDeclaratorWithShuffle(int[] a, int x, int[] result)
    {
        int l = ThreadIndex.X;
        int p = Bump(a, l), q = p + Warp.ShuffleSync(0xffffffffu, x, 0);
        result[l] = q;
    }

    [GpuKernel]
    public static void OperandWithoutNaturalType(bool c, long[] result)
    {
        result[0] = TakeLong(c ? 1u : -1, Suspend());
    }

    [GpuKernel]
    public static void EarlierArgumentOfACallThatDoesNotSplit(int[] a, int[] result)
    {
        result[0] = Pair(a[0], Suspend());
    }

    [GpuKernel]
    public static void CheckOfAFieldTarget(Cell[] cells, int[] b)
    {
        int i = ThreadIndex.X;
        cells[b[i]].F = Suspend();
    }

    [GpuKernel]
    public static void CompoundLocal(int[] result)
    {
        int x = ThreadIndex.X;
        x += Suspend();
        result[0] = x;
    }

    [GpuKernel]
    public static void DiscardedValueOfASplit()
    {
        Suspend();
    }

    [GpuKernel]
    public static void TwoSplitStatements(int[] a, int[] result)
    {
        result[0] = a[0] + Suspend();
        result[1] = a[1] + Suspend();
    }

    [GpuKernel]
    public static void MixedIrAndLoopSplits(int[] a, int[] result)
    {
        int l = ThreadIndex.X;
        result[l] = a[l] + Suspend();
        for (int k = 0; k < 2; k++)
        {
            result[k] = a[k] + Suspend();
        }
    }
}
