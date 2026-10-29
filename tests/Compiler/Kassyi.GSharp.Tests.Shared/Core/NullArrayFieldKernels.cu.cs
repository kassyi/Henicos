using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct with an array field that a caller may leave null or set to an empty array.</summary>
public struct NullArrayHolder
{
    public int[] Data;
    public int X;
}

/// <summary>Two array fields, for the interplay with one device region per host array (#707).</summary>
public struct NullArrayPair
{
    public int[] A;
    public int[] B;
}

public static partial class NullArrayFieldKernels
{
    // C#: reading Length of a null array throws NullReferenceException
    [GpuKernel]
    public static void LengthKernel(NullArrayHolder h, int[] o)
    {
        o[0] = h.Data.Length + 7;
    }

    // C#: NullReferenceException, not IndexOutOfRangeException
    [GpuKernel]
    public static void ReadElementKernel(NullArrayHolder h, int[] o)
    {
        o[0] = h.Data[0];
    }

    [GpuKernel]
    public static void WriteElementKernel(NullArrayHolder h, int[] o)
    {
        h.Data[0] = 1;
        o[0] = 2;
    }

    [GpuKernel]
    public static void ForEachKernel(NullArrayHolder h, int[] o)
    {
        foreach (var v in h.Data)
        {
            o[0] += v;
        }
    }

    // The check follows the value: a local and a device function parameter that hold the null field fail too
    [GpuKernel]
    public static void LocalToDeviceFunctionKernel(NullArrayHolder h, int[] o)
    {
        int[] d = h.Data;
        o[0] = Len(d);
    }

    [GpuKernel]
    public static void DefaultLocalKernel(int[] o)
    {
        NullArrayHolder l = default;
        o[0] = l.Data.Length;
    }

    // An empty array is not null: Length is 0 and `is null` is false
    [GpuKernel]
    public static void EmptyFieldKernel(NullArrayHolder h, int[] o)
    {
        o[0] = h.Data.Length + 7;
        o[1] = h.Data is null ? 1 : 0;
    }

    // A null field that the kernel does not read is no error
    [GpuKernel]
    public static void UnreadNullFieldKernel(NullArrayHolder h, int[] o)
    {
        o[0] = h.X;
        o[1] = h.Data is null ? 1 : 0;
    }

    // C#: a null array converts to an empty span
    [GpuKernel]
    public static void SpanKernel(NullArrayHolder h, int[] o)
    {
        ReadOnlySpan<int> s = h.Data;
        o[0] = s.Length;
        foreach (var v in s)
        {
            o[0] += v;
        }
    }

    // A null optional argument reads as an empty array, also through a device function (#838)
#nullable enable
    [GpuKernel]
    public static void OptionalToDeviceFunctionKernel(int[] o, int[]? bias)
    {
#pragma warning disable CS8604 // The device reads a null optional array as one of length 0
        o[0] = Len(bias);
#pragma warning restore CS8604
    }
#nullable restore

    [GpuKernel]
    public static void StructArrayKernel(NullArrayHolder[] holders, int[] o)
    {
        o[0] = holders[0].Data.Length;
        o[1] = holders[1].Data.Length;
    }

    // C#: A and B are different arrays; B is empty, not null
    [GpuKernel]
    public static void SharedEmptyKernel(NullArrayPair p, int[] o)
    {
        p.A[0] = 9;
        o[0] = p.B.Length;
        o[1] = p.B is null ? 1 : 0;
    }

    [GpuDeviceFunction]
    private static int Len(int[] a) => a.Length;

    // A foreach that holds a barrier is lowered to a for loop over a copy of the source; the check moves with it
    [GpuKernel]
    public static void ForEachWithBarrierKernel(NullArrayHolder h, int[] o)
    {
        foreach (var v in h.Data)
        {
            Gpu.SyncThreads();
            o[0] += v;
        }
    }
}
