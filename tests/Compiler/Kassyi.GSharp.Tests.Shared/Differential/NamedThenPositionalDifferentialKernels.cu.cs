#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for calls that name an argument in its own position and pass the next one by position
/// (<c>Sub(a: 1, 5)</c>): the positional argument is passed to the parameter at its position, as in C#.
/// </summary>
public static partial class NamedThenPositionalDifferentialKernels
{
    public static readonly string[] Columns =
        ["with_default", "without_default", "extension", "middle_named", "optional_omitted"];

    [GpuDeviceFunction]
    public static int SubWithDefault(int a, int b = 10) => a - b;

    [GpuDeviceFunction]
    public static int SubWithoutDefault(int a, int b) => a - b;

    [GpuDeviceFunction]
    public static int Mix(int a, int b, int c = 7) => (a * 100) + (b * 10) + c;

    [GpuDeviceFunction]
    public static int AddOptional(int a, [Optional] int b) => (a * 10) + b;

    [GpuDeviceFunction]
    public static int SubAcrossBarrier(int a, int b = 10)
    {
        Gpu.SyncThreads();
        return a - b;
    }

    [GpuKernel]
    public static void NamedThenPositionalKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 5;
        r[o + 0] = SubWithDefault(a: x, y);
        r[o + 1] = SubWithoutDefault(a: x, y);
        r[o + 2] = x.Scale(factor: 3, y);
        r[o + 3] = Mix(x & 7, b: y & 7, (x ^ y) & 7);
        r[o + 4] = AddOptional(a: x);
    }

    // A device function that reaches a barrier is called through the resume protocol
    [GpuKernel]
    public static void NamedThenPositionalSuspendingCallKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        r[tid] = SubAcrossBarrier(a: x, y);
    }
}

// Internal, so that the generator snapshot, which compiles this file next to the test assembly, sees one Scale
internal static partial class NamedThenPositionalExtensions
{
    [GpuDeviceFunction]
    public static int Scale(this int value, int factor, int bias = 1000) => (value * factor) + bias;
}
