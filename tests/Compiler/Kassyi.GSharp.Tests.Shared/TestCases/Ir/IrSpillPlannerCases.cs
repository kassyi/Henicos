using System;
using Kassyi.GSharp.Core;

namespace IrSpillPlannerCases;

public struct Cell
{
    public int F;

    public int Add(int v)
    {
        F += v;
        return F;
    }

    public readonly int Peek(int v) => F + v;
}

public struct Wrapped
{
    public int V;

    public static implicit operator Wrapped(int v) => new Wrapped { V = v };
}

public enum Lane
{
    A,
    B
}

public static class Extensions
{
    [GpuDeviceFunction]
    public static int AfterBarrier(this int x)
    {
        Gpu.SyncThreads();
        return x;
    }
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

    [GpuDeviceFunction]
    public static int Triple(int a, int b, int c) => a + b + c;

    [GpuKernel]
    public static void EarlierOperandBeforeAnUnconditionalSplit(int[] a, int x, bool c, int[] result)
    {
        result[0] = Triple(a[0], Warp.ShuffleSync(0xffffffffu, x, 0), c ? Suspend() : 0);
    }

    [GpuKernel]
    public static void SplitInLoop(int[] result)
    {
        for (int i = 0; i < 2; i++)
        {
            result[i] = Suspend();
        }
    }

    [GpuDeviceFunction]
    public static void SuspendRef(ref int target, int value)
    {
        Gpu.SyncThreads();
        target = value;
    }

    [GpuDeviceFunction]
    public static int Take(Wrapped w, int b) => w.V + b;

    [GpuKernel]
    public static void SpanElementTarget(int[] data)
    {
        Span<int> s = data.AsSpan();
        int i = ThreadIndex.X;
        s[i] = Suspend();
    }

    [GpuKernel]
    public static void ElementTarget(int[] a)
    {
        int i = ThreadIndex.X;
        a[i] = Suspend();
    }

    [GpuKernel]
    public static void StructMethodOfElement(Cell[] cells, int[] result)
    {
        int i = ThreadIndex.X;
        result[0] = cells[i].Add(Suspend());
    }

    [GpuKernel]
    public static void RefElementOfASplit(int[] a)
    {
        int i = ThreadIndex.X;
        SuspendRef(ref a[i], 1);
    }

    [GpuKernel]
    public static void CompoundThenReadSameElement(int[] a, int x, int y, int[] r)
    {
        int l = ThreadIndex.X;
        r[l] = Triple(a[l] += Warp.ShuffleSync(0xffffffffu, x, 0), a[l], Warp.ShuffleSync(0xffffffffu, y, 0));
    }

    [GpuKernel]
    public static void NestedCompound(int[] a, int[] b, int x)
    {
        int l = ThreadIndex.X;
        a[l] += b[l] += Warp.ShuffleSync(0xffffffffu, x, 0);
    }

    [GpuKernel]
    public static void CompoundFieldOfElement(Cell[] cells)
    {
        int i = ThreadIndex.X;
        cells[i].F += Suspend();
    }

    [GpuDeviceFunction]
    public static int ReadOnlyReceiverOfAMutatingMethod(in Cell c) => c.Add(Suspend());

    [GpuDeviceFunction]
    public static int ReadOnlyReceiverOfAReadOnlyMethod(in Cell c) => c.Peek(Suspend());

    [GpuKernel]
    public static void IndexNumericConversion(int[] r, long n)
    {
        r[(int)n] = Suspend();
    }

    [GpuKernel]
    public static void IndexEnumConversion(int[] r, Lane lane)
    {
        r[(int)lane] = Suspend();
    }

    [GpuKernel]
    public static void IndexThreadIndex(int[] r)
    {
        r[ThreadIndex.X + 1] = Suspend();
    }

    [GpuKernel]
    public static void IndexNullableConversion(int[] r, int? n)
    {
        r[(int)n] = Suspend();
    }

    [GpuKernel]
    public static void IndexUnboxing(int[] r, object o)
    {
        r[(int)o] = Suspend();
    }

    [GpuKernel]
    public static void IndexCheckedArithmetic(int[] r, int u)
    {
        int t = ThreadIndex.X;
        r[checked(t + u)] = Suspend();
    }

    [GpuKernel]
    public static void IndexArrayRead(int[] r, int[] idx)
    {
        r[idx[0]] = Suspend();
    }

    [GpuKernel]
    public static void CoalesceRight(int?[] cached, int[] data)
    {
        int t = ThreadIndex.X;
        data[t] = cached[t] ?? Warp.ShuffleSync(0xffffffffu, data[t], 0);
    }

    [GpuKernel]
    public static void ExtensionReceiverOfASplit(int[] data)
    {
        int x = ThreadIndex.X;
        data[0] = (x - 1).AfterBarrier();
    }

    [GpuKernel]
    public static void UserDefinedConversionBeforeASplit(int[] a, int[] result)
    {
        result[0] = Take(a[0], Suspend());
    }

    [GpuKernel]
    public static void OrElseDeclarator(int[] result)
    {
        bool p = ThreadIndex.X > 0;
        bool q = p || Suspend() > 0;
        result[0] = q ? 1 : 0;
    }

    [GpuKernel]
    public static void DiscardedSuspend()
    {
        Suspend();
    }

    [GpuKernel]
    public static void BarrierStatement()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    public static void DiscardedShuffle(int x)
    {
        Warp.ShuffleSync(0xffffffffu, x, 0);
    }

    [GpuKernel]
    public static void IfConditionSplit(int[] result)
    {
        if (Suspend() > 0)
        {
            result[0] = Suspend();
        }
    }

    [GpuDeviceFunction]
    public static int ReturnAfterSplit(int[] a)
    {
        return a[0] + Suspend();
    }

    [GpuKernel]
    public static void ConditionalBothArmsSplit(bool c, int x, int[] result)
    {
        result[0] = c ? Suspend() : Warp.ShuffleSync(0xffffffffu, x, 0);
    }

    [GpuKernel]
    public static void CheckReadsTheIndexOnce(Cell[] cells, int[] b)
    {
        cells[b[0]].F = Suspend();
    }
}
