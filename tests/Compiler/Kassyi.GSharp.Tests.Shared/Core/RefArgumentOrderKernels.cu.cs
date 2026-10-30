// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>An element of the arrays that <see cref="RefArgumentOrderKernels"/> pass a field of by reference.</summary>
public struct RefOrderCell
{
    public int F;
    public int G;
}

/// <summary>A struct whose array field selects the element a ref argument names.</summary>
public struct RefOrderHolder
{
    public int[] Data;
}

/// <summary>
/// Calls of device functions that suspend, with by-reference arguments (#788). C# fixes the storage of a ref, out or in
/// argument, and the value of every other argument, when it evaluates the argument, once; the CPU paths run the call
/// again on every resume, so what C# evaluated must not be evaluated again.
/// </summary>
public static partial class RefArgumentOrderKernels
{
    // A field of an element: C# selects cells[j] with j before the later j++
    [GpuKernel]
    public static void FieldOfElementIndexWrittenLaterKernel(RefOrderCell[] cells)
    {
        int j = ThreadIndex.X * 2;
        AddAfterBarrier(ref cells[j].F, j++);
    }

    // An index read from memory that the callee changes before its barrier: C# selects the element once
    [GpuKernel]
    public static void IndexFromMemoryChangedByCalleeKernel(RefOrderCell[] cells, int[] idx)
    {
        AddAfterChangingIndex(ref cells[idx[ThreadIndex.X]].F, idx);
    }

    // The array of a ref argument, replaced by a later argument: C# writes the element of the old array
    [GpuKernel]
    public static void ArrayReplacedByLaterArgumentKernel(int[] data, int[] other)
    {
        int[] d = data;
        AddAfterBarrier(ref d[ThreadIndex.X], (d = other).Length);
    }

    // The same with the array in a struct field
    [GpuKernel]
    public static void ArrayFieldReplacedByLaterArgumentKernel(int[] data, int[] other)
    {
        RefOrderHolder holder;
        holder.Data = data;
        AddAfterBarrier(ref holder.Data[ThreadIndex.X], (holder.Data = other).Length);
    }

    // The index of a ref argument, passed by ref in a later argument that the callee increments before its barrier
    [GpuKernel]
    public static void IndexPassedByRefLaterKernel(int[] data)
    {
        int i = ThreadIndex.X * 2;
        IncrementSecondThenAdd(ref data[i], ref i);
    }

    // The same with the index passed by ref in an earlier argument
    [GpuKernel]
    public static void IndexPassedByRefEarlierKernel(int[] data)
    {
        int i = ThreadIndex.X * 2;
        IncrementFirstThenAdd(ref i, ref data[i]);
    }

    // A value argument whose variable an earlier argument passes by ref: C# passes the value before the callee writes x
    [GpuKernel]
    public static void ValueOfVariablePassedByRefKernel(int[] result)
    {
        int x = 7;
        WriteThenRead(ref x, x, result);
    }

    // A value argument read through a ref parameter, whose storage the callee writes through another name
    [GpuKernel]
    public static void ValueReadThroughRefParameterKernel(int[] data, int[] result)
    {
        PassOn(ref data[ThreadIndex.X], data, result);
    }

    // An in parameter aliases its variable: the later x++ changes what the callee reads (C#: p == 2)
    [GpuKernel]
    public static void InParameterSeesLaterWriteKernel(int[] result)
    {
        int x = 1;
        ReadInAfterBarrier(x, x++, result);
    }

    // An in parameter of an element aliases the element: the callee's write is seen through p (C#: p == 5)
    [GpuKernel]
    public static void InParameterOfElementKernel(int[] data, int[] result)
    {
        WriteElementThenReadIn(data[ThreadIndex.X], data, result);
    }

    // An int expression passed to a long parameter: the spilled temporary is a long
    [GpuKernel]
    public static void IntExpressionToLongParameterKernel(long[] result)
    {
        int i = ThreadIndex.X;
        result[ThreadIndex.X] = WaitAndReturn(i * 3 + 1);
    }

    [GpuDeviceFunction]
    private static void AddAfterBarrier(ref int target, int value)
    {
        Gpu.SyncThreads();
        target += value + 1000;
    }

    [GpuDeviceFunction]
    private static void AddAfterChangingIndex(ref int target, int[] idx)
    {
        idx[ThreadIndex.X] = 63 - ThreadIndex.X;
        Gpu.SyncThreads();
        target += 1000;
    }

    [GpuDeviceFunction]
    private static void IncrementSecondThenAdd(ref int target, ref int i)
    {
        i++;
        Gpu.SyncThreads();
        target += 1000;
    }

    [GpuDeviceFunction]
    private static void IncrementFirstThenAdd(ref int i, ref int target)
    {
        i++;
        Gpu.SyncThreads();
        target += 1000;
    }

    [GpuDeviceFunction]
    private static void WriteThenRead(ref int x, int v, int[] result)
    {
        x = 5;
        Gpu.SyncThreads();
        result[ThreadIndex.X] = v;
    }

    [GpuDeviceFunction]
    private static void PassOn(ref int p, int[] data, int[] result)
    {
        WriteElementThenRead(p, data, result);
    }

    [GpuDeviceFunction]
    private static void WriteElementThenRead(int v, int[] data, int[] result)
    {
        data[ThreadIndex.X] = 99;
        Gpu.SyncThreads();
        result[ThreadIndex.X] = v;
    }

    [GpuDeviceFunction]
    private static void ReadInAfterBarrier(in int p, int q, int[] result)
    {
        Gpu.SyncThreads();
        result[ThreadIndex.X] = p * 10 + q;
    }

    [GpuDeviceFunction]
    private static void WriteElementThenReadIn(in int p, int[] data, int[] result)
    {
        data[ThreadIndex.X] = 5;
        Gpu.SyncThreads();
        result[ThreadIndex.X] = p;
    }

    [GpuDeviceFunction]
    private static long WaitAndReturn(long x)
    {
        Gpu.SyncThreads();
        return x;
    }
}
