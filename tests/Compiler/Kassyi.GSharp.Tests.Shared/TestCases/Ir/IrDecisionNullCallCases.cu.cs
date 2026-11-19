#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Core;

// Calls of device functions from a method without a GPU attribute, read by IrNullDecisionTests: the error site of an
// argument check is the argument as written, and a removed call plans no check and no conversion (#1561).

public struct DefaultHolder { public int[] Data; }

public static class CallSites
{
    [GpuDeviceFunction]
    private static int ElementOf(int[] values, int index) => values[index];

    public static void Call(int[]? b, int i, int[] r)
    {
        r[0] = ElementOf(b!, i);
        r[1] = ElementOf((b!), i);
        r[2] = ElementOf(null!, i);
    }
}

public static class DefaultCallSites
{
    [GpuDeviceFunction]
    private static int ElementOf(int[] values, int index) => values[index];

    [GpuDeviceFunction]
    private static int ElementOfObj(object[] values, int index) => values.Length + index;

    [GpuDeviceFunction]
    private static int ElementOfHolder(DefaultHolder holder, int index) => holder.Data[index];

    public static void Call(int i, int[] r)
    {
        r[0] = ElementOf(default!, i);
        r[1] = ElementOf((default!), i);
        r[2] = ElementOf((default), i);
        r[3] = ElementOfObj(default!, i);
        r[4] = ElementOfHolder(default!, i);
    }
}

public static partial class RemovedDeviceCalls
{
    [GpuDeviceFunction]
    [Conditional("NEVER_DEFINED")]
    private static void ConditionalDevice(int[] values, int count) { }

    [GpuDeviceFunction]
    static partial void PartialDevice(int[] values, int count);

    public static void Call(int[] values, byte count)
    {
        ConditionalDevice(values, count);
        PartialDevice(values, count);
    }
}
