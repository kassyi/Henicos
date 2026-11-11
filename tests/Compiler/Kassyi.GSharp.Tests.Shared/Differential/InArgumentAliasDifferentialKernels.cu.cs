#nullable enable
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for a variable passed to an <c>in</c> (or <c>ref readonly</c>) parameter of a device function
/// that reaches a barrier, mostly without the <c>in</c> keyword: C# passes the variable itself, so the callee reads
/// through the parameter what it wrote through its <c>ref</c> parameter before the barrier (#1585). Each kernel passes
/// one shape of variable; <c>r[tid]</c> is what the callee read.
/// </summary>
public static unsafe partial class InArgumentAliasDifferentialKernels
{
    // Writes target, waits at the barrier, then reads x: x + 7 when x is target, x when x is a copy
    [GpuDeviceFunction]
    public static int ReadAfterWrite(in int x, ref int target)
    {
        int before = x;
        target = before + 7;
        Gpu.SyncThreads();
        return x;
    }

    [GpuDeviceFunction]
    public static int ReadOnlyAfterWrite(ref readonly int x, ref int target)
    {
        int before = x;
        target = before + 7;
        Gpu.SyncThreads();
        return x;
    }

    // Also moves the index the caller selected x with, which must not change the variable x is
    [GpuDeviceFunction]
    public static int ReadAfterWriteMovingIndex(in int x, ref int target, ref int index)
    {
        int before = x;
        target = before + 7;
        index = 1;
        Gpu.SyncThreads();
        return x;
    }

    // An inline array value; counts its calls in calls[slot], so a call evaluated again on a resume shows
    [GpuDeviceFunction]
    public static AliasInline4 MakeInline(int value, int[] calls, int slot)
    {
        calls[slot] += 1;
        AliasInline4 b = default;
        b[0] = value;
        return b;
    }

    [GpuKernel]
    public static void InlineElementKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasInline4 buf = default;
        buf[0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(buf[0], ref buf[0]);
    }

    [GpuKernel]
    public static void ParenthesizedInlineElementKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasInline4 buf = default;
        buf[0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite((buf[0]), ref buf[0]);
    }

    [GpuKernel]
    public static void ParenthesizedLocalKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int v = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite((v), ref v);
    }

    [GpuKernel]
    public static void FixedBufferElementKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasFixed4 fixedBuf = default;
        fixedBuf.Buf[0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(fixedBuf.Buf[0], ref fixedBuf.Buf[0]);
    }

    [GpuKernel]
    public static void ReadOnlyReferenceKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasInline4 buf = default;
        buf[0] = xs[tid] & 0x3FF;
#pragma warning disable CS9192 // The argument of a ref readonly parameter is given without the keyword on purpose
        r[tid] = ReadOnlyAfterWrite(buf[0], ref buf[0]);
#pragma warning restore CS9192
    }

    [GpuKernel]
    public static void StructFieldInlineElementKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasHolder holder = default;
        holder.Inner[0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(holder.Inner[0], ref holder.Inner[0]);
    }

    [GpuKernel]
    public static void NestedInlineElementKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasOuter2 outer = default;
        outer[1][0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(outer[1][0], ref outer[1][0]);
    }

    // The callee moves i after C# selected buf[0]; buf[1] holds a value the callee must not read
    [GpuKernel]
    public static void IndexMovedByCalleeKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasInline4 buf = default;
        buf[0] = xs[tid] & 0x3FF;
        buf[1] = 5000;
        int i = 0;
        r[tid] = ReadAfterWriteMovingIndex(buf[i], ref buf[0], ref i);
    }

    // An element of an inline array value is a value: C# copies it once, so r[tid] is x and MakeInline runs once
    [GpuKernel]
    public static void ValueElementKernel(int[] xs, int[] r, int[] calls)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int other = x;
        r[tid] = ReadAfterWrite(MakeInline(x, calls, tid)[0], ref other);
    }

    // g holds two ints per thread: the first is the variable, the second must stay as it is
    [GpuKernel]
    public static void PointerIndirectionKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = g + (tid * 2);
        *p = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(*p, ref *p);
    }

    [GpuKernel]
    public static void PointerElementKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = g + (tid * 2);
        p[0] = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(p[0], ref p[0]);
    }

    [GpuKernel]
    public static void PointerMemberKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasPair* cell = (AliasPair*)(g + (tid * 2));
        cell->First = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite(cell->First, ref cell->First);
    }

    [GpuKernel]
    public static void DereferencedMemberKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        AliasPair* cell = (AliasPair*)(g + (tid * 2));
        (*cell).First = xs[tid] & 0x3FF;
        r[tid] = ReadAfterWrite((*cell).First, ref (*cell).First);
    }

    // r[tid] of the moved pointer kernels counts how far p moved: once, as C# evaluates p++ once
    public const int MovedPointerScale = 10000;

    // The second argument moves p after C# selected p[0] (#1173): both arguments name the first int of the thread
    [GpuKernel]
    public static void PointerMovedByLaterArgumentKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* start = g + (tid * 2);
        int* p = start;
        p[0] = xs[tid] & 0x3FF;
        int read = ReadAfterWrite(p[0], ref *p++);
        r[tid] = read + ((int)(p - start) * MovedPointerScale);
    }

    // The same with the in keyword
    [GpuKernel]
    public static void PointerMovedByLaterKeywordArgumentKernel(int* g, int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* start = g + (tid * 2);
        int* p = start;
        p[0] = xs[tid] & 0x3FF;
        int read = ReadAfterWrite(in p[0], ref *p++);
        r[tid] = read + ((int)(p - start) * MovedPointerScale);
    }
}

[InlineArray(4)]
public struct AliasInline4
{
    private int _element;
}

[InlineArray(2)]
public struct AliasOuter2
{
    private AliasInline4 _element;
}

public struct AliasHolder
{
    public int Pad;
    public AliasInline4 Inner;
}

public unsafe struct AliasFixed4
{
    public fixed int Buf[4];
}

public struct AliasPair
{
    public int First;
    public int Second;
}
