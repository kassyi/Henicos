using System;
using Kassyi.GSharp.Core;

namespace IrSpillPlannerCases;

public struct Cell
{
    public int F;
}

// Each kernel ends with the statement the test plans; the split points are a warp shuffle and device functions that
// suspend (they reach a barrier)
public static class Cases
{
    [GpuDeviceFunction]
    public static int Suspend()
    {
        Gpu.SyncThreads();
        return 1;
    }

    [GpuDeviceFunction]
    public static int SuspendWith(int a, int b)
    {
        Gpu.SyncThreads();
        return a + b;
    }

    [GpuDeviceFunction]
    public static int Bump(int[] data, int i) => data[i] += 7;

    [GpuDeviceFunction]
    public static int Inc(ref int x) => ++x;

    [GpuDeviceFunction]
    public static int Pair(int a, int b) => a - b;

    [GpuDeviceFunction]
    public static int Use(Span<int> span, int b) => span[0] + b;

    [GpuKernel]
    public static void LeftOperandWrittenBeforeShuffle(int[] a, int[] result)
    {
        int l = ThreadIndex.X;
        result[l] = (a[l] = l + 100) + Warp.ShuffleSync(0xffffffffu, a[l], 0);
    }

    [GpuKernel]
    public static void LocalLeftOperandStays(int x, int y, int[] result)
    {
        result[0] = x + Warp.ShuffleSync(0xffffffffu, y, 0);
    }

    [GpuKernel]
    public static void CompoundElementTarget(int[] a)
    {
        int l = ThreadIndex.X;
        a[l] += Warp.ShuffleSync(0xffffffffu, Bump(a, l), 0);
    }

    [GpuKernel]
    public static void CompoundLocalTarget(int[] result)
    {
        int x = ThreadIndex.X;
        x += Warp.ShuffleSync(0xffffffffu, Inc(ref x), 0);
        result[0] = x;
    }

    [GpuKernel]
    public static void EarlierArgumentOfACallThatDoesNotSplit(int[] a, int[] result)
    {
        result[0] = Pair(a[0], Suspend());
    }

    [GpuKernel]
    public static void ArgumentLeftOfAConditionalSplit(int[] a, bool c, int[] result)
    {
        result[0] = SuspendWith(a[0], c ? Suspend() : 0);
    }

    [GpuKernel]
    public static void LaterDeclarator(int[] result)
    {
        int tid = ThreadIndex.X;
        bool p = tid > 0, q = p && Suspend() > 0;
        result[tid] = q ? 1 : 0;
    }

    [GpuKernel]
    public static void NamedArgumentsInWrittenOrder(int[] result)
    {
        int i = ThreadIndex.X;
        result[0] = SuspendWith(b: Inc(ref i), a: i);
    }

    [GpuKernel]
    public static void SpanLocalStays(int[] data, int[] result)
    {
        Span<int> span = data.AsSpan();
        result[0] = Use(span, Suspend());
    }

    [GpuKernel]
    public static void SpanValueBeforeSplit(int[] data, int[] result)
    {
        Span<int> span = data.AsSpan();
        result[0] = Use(span.Slice(1), Suspend());
    }

    [GpuDeviceFunction]
    public static int UseAfterBarrier(Span<int> span, int b)
    {
        Gpu.SyncThreads();
        return span[0] + b;
    }

    [GpuDeviceFunction]
    public static int Reassign(ref Span<int> span, int[] data)
    {
        span = data.AsSpan(1);
        return 0;
    }

    [GpuKernel]
    public static void SpanLocalWrittenByALaterArgument(int[] data, int[] result)
    {
        Span<int> span = data.AsSpan();
        result[0] = UseAfterBarrier(span, Reassign(ref span, data));
    }

    [GpuKernel]
    public static void LeftOperandOfAConditionalSplit(int[] a, bool c, int[] result)
    {
        result[0] = a[0] + (c ? Suspend() : 0);
    }

    [GpuDeviceFunction]
    public static int Store(ref int target, int value)
    {
        target = value;
        return value;
    }

    [GpuKernel]
    public static void TargetIndexOverLocals(uint[] r)
    {
        int t = ThreadIndex.X;
        r[t + 32] = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void TargetIndexThatCanThrow(int[] r, int u)
    {
        int t = ThreadIndex.X;
        r[t / u] = Suspend();
    }

    [GpuKernel]
    public static void TargetIndexWrittenByTheValue(int[] r)
    {
        int t = ThreadIndex.X;
        r[t + 1] = SuspendWith(Inc(ref t), 0);
    }

    [GpuKernel]
    public static void StructElementFieldTarget(Cell[] cells, int[] b)
    {
        int i = ThreadIndex.X;
        cells[b[i]].F = Suspend();
    }

    [GpuKernel]
    public static void RefElementArgumentOfACallThatDoesNotSplit(int[] a, int[] b, int[] result)
    {
        int i = ThreadIndex.X;
        result[0] = Store(ref a[b[i]], Suspend());
    }

    [GpuKernel]
    public static void SplitInLoop(int[] result)
    {
        for (int i = 0; i < 2; i++)
        {
            result[i] = Suspend();
        }
    }
}
