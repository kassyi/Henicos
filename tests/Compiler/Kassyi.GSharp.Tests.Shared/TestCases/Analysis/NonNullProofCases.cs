#nullable enable
using Kassyi.GSharp.Core;

namespace NonNullProofCases;

public struct Buffers
{
    public int[] Data;
    public int[]? Optional;

    public void Swap() => (Data, Optional) = (Optional ?? Data, Data);
}

public struct Holder
{
    public Buffers Inner;
    public int Count;
}

#nullable disable
public struct Oblivious
{
    public int[] Data;
}
#nullable enable

public static class Reads
{
    // Each method marks the read it is about with the local `read`, which the test finds by name

    [GpuKernel]
    public static void KernelStructField(Holder holder, int[] output)
    {
        var read = holder.Inner.Data;
        output[0] = read.Length;
    }

    [GpuKernel]
    public static void KernelNullableField(Holder holder, int[] output)
    {
        var read = holder.Inner.Optional;
        output[0] = read?.Length ?? 0;
    }

    [GpuKernel]
    public static void KernelWrittenStruct(Holder holder, Buffers other, int[] output)
    {
        holder.Inner = other;
        var read = holder.Inner.Data;
        output[0] = read.Length;
    }

    [GpuKernel]
    public static void KernelObliviousField(Oblivious item, int[] output)
    {
        var read = item.Data;
        output[0] = read.Length;
    }

    [GpuKernel]
    public static void KernelElementField(Buffers[] items, int[] output)
    {
        var read = items[0].Data;
        output[0] = read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceParameter(int[] data)
    {
        var read = data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceNullableParameter(int[]? data)
    {
        var read = data;
        return read?.Length ?? 0;
    }

    [GpuDeviceFunction]
    public static int DeviceWrittenParameter(int[] data, int[] other)
    {
        data = other;
        var read = data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceDefaultParameter(int[] data = null!)
    {
        var read = data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceStructField(Buffers buffers)
    {
        var read = buffers.Data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceMutatedStruct(Buffers buffers)
    {
        buffers.Swap();
        var read = buffers.Data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceLocalCopy(Holder holder)
    {
        var inner = holder.Inner;
        var read = inner.Data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceReassignedLocal(Holder holder, Buffers other)
    {
        var inner = holder.Inner;
        inner = other;
        var read = inner.Data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int DeviceNewStruct(int[] data)
    {
        var made = new Buffers { Data = data };
        var read = made.Data;
        return read.Length;
    }

    [GpuDeviceFunction]
    public static int ExtensionParameter(this int[] data)
    {
        var read = data;
        return read.Length;
    }
}
