using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct ReadOnlyWriteHolder
{
    [GpuReadOnly] public uint[] Fixed;
    public uint[] Free;
}

public struct ReadOnlyAliasHolder
{
    public uint[] Data;
}

// GSH0118: a write to a [GpuReadOnly] array that the compiler can see
public static partial class ReadOnlyArrayWriteKernels
{
    [GpuKernel]
    public static void DirectKernel([GpuReadOnly] uint[] r, ReadOnlyWriteHolder h, uint[] o)
    {
        r[0] = 5u;
        h.Fixed[1] = 6u;
        r[2]++;
        --r[3];
        r[4] += 7u;
        h.Free[0] = 1u;
        o[0] = r[0] + h.Fixed[1];
    }

    [GpuKernel]
    public static void ByReferenceKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        Increment(ref r[0]);
        Increment(ref o[0]);
    }

    [GpuKernel]
    public static void ConvertedKernel([GpuReadOnly] uint[] r, ReadOnlyWriteHolder h, uint[] o)
    {
        uint[] x = h.Fixed;
        x[0] = 1u;
        Span<uint> view = r.AsSpan();
        view[1] = 2u;
        Span<uint> sliced = Gpu.NotNull(r).AsSpan().Slice(1);
        sliced[0] = 3u;
        Span<uint> own = o;
        own[0] = 4u;
        o[1] = x[1] + view[0];
    }

    [GpuKernel]
    public static void MethodKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        r.AsSpan().Fill(5u);
        Array.Fill(r, 6u);
        Array.Copy(o, r, 1);
        o.AsSpan().CopyTo(r.AsSpan());
        r.AsSpan().CopyTo(o.AsSpan());
        Array.Reverse(r);
        Array.Fill(o, 7u);
    }

    [GpuKernel]
    public static void DeviceFunctionKernel([GpuReadOnly] uint[] r, ReadOnlyWriteHolder h, uint[] o)
    {
        WriteFirst(r);
        WriteFirst(h.Fixed);
        WriteFirst(o);
        o[0] = ReadFirst(r) + ReadFirst(h.Fixed);
    }

    [GpuKernel]
    public static void StructAliasKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var assigned = new ReadOnlyAliasHolder();
        assigned.Data = r;
        assigned.Data[0] = 4u;
        var initialized = new ReadOnlyAliasHolder { Data = r };
        initialized.Data[1] = 12u;
        var writable = new ReadOnlyAliasHolder { Data = o };
        writable.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefForEachKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        foreach (ref uint element in r.AsSpan())
        {
            element = 1u;
        }

        foreach (ref uint element in o.AsSpan())
        {
            element = 2u;
        }

        foreach (ref readonly uint element in r.AsSpan())
        {
            o[0] = element;
        }
    }

    // The callee writes its second parameter, which is [GpuReadOnly] itself: it is reported there, not at the call
    [GpuKernel]
    public static void SecondParameterKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        WriteSecond(r, o);
    }

    // A parameter that is given another array before the write is not the read-only array any more
    [GpuKernel]
    public static void ReassignedParameterKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        r = o;
        r[0] = 15u;
    }

    [GpuDeviceFunction]
    private static void WriteSecond(uint[] first, [GpuReadOnly] uint[] second)
    {
        second[0] = first[0];
    }

    [GpuDeviceFunction]
    private static void Increment(ref uint value)
    {
        value++;
    }

    [GpuDeviceFunction]
    private static void WriteFirst(uint[] array)
    {
        array[0] = 9u;
    }

    [GpuDeviceFunction]
    private static uint ReadFirst(uint[] array) => array[0];
}
