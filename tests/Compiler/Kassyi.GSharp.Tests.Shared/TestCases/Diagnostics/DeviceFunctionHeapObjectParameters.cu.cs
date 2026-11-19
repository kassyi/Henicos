using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: object, Delegate, MulticastDelegate and 1D arrays of them in device function parameters
[SkipSnapshotConsistency("Diagnostics case")]
public static partial class DeviceFunctionHeapObjectParameterKernels
{
    [GpuDeviceFunction]
    public static int Take(object o) => 1;

    [GpuDeviceFunction]
    public static int TakeRef(ref object o) => 1;

    [GpuDeviceFunction]
    public static int TakeIn(in object o) => 1;

    [GpuDeviceFunction]
    public static int TakeDelegate(Delegate d) => 1;

    [GpuDeviceFunction]
    public static int TakeMulticastDelegate(MulticastDelegate d) => 1;

    [GpuDeviceFunction]
    public static int TakeArray(object[] a) => 1;

    [GpuDeviceFunction]
    public static int TakeInt(int x) => x;

    [GpuDeviceFunction]
    public static int TakeSpan(Span<int> s) => s.Length;

    [GpuKernel]
    public static void CallTakeKernel(int[] output)
    {
        output[0] = Take(5);
    }

    // A kernel is reported once, as GSH0112, and not again as GSH0003
    [GpuKernel]
    public static void ObjectParameterKernel(object o, int[] output)
    {
        output[0] = 1;
    }

    [GpuKernel]
    public static void DelegateParameterKernel(Delegate d, int[] output)
    {
        output[0] = 1;
    }
}
