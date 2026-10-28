// ReSharper disable All
using System.Threading;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels for <see cref="GridStrideLoopScenarios"/> (#712): <c>Gpu.GridStrideLoop</c> visits each index of
/// <c>[0, count)</c> once across every thread of the launch, on every axis, and evaluates its count once.
/// </summary>
public static partial class GridStrideLoopKernels
{
    // The increment is atomic, so that two threads visiting the same index both count
    [GpuKernel]
    public static void CountVisitsKernel(int[] visits, int count)
    {
        Gpu.GridStrideLoop(count, i => { Interlocked.Add(ref visits[i], 1); });
    }

    [GpuKernel]
    public static void CountVisitsWithContextKernel(int[] visits, int count)
    {
        int unused = 0;
        Gpu.GridStrideLoop(ref unused, count, (ref u, i) => { Interlocked.Add(ref visits[i], 1); });
    }

    // The body clears the array the count was read from: C# reads the count once, before the loop
    [GpuKernel]
    public static void CountReadOnceKernel(int[] length, int[] output)
    {
        Gpu.GridStrideLoop(length[0], i =>
        {
            if (i == 0)
            {
                length[0] = 0;
            }

            output[i] = 1;
        });
    }

    // The count is a call with a side effect: each thread makes it once
    [GpuKernel]
    public static void CountCallOnceKernel(int[] calls, int[] visits, int count)
    {
        Gpu.GridStrideLoop(CountedLength(calls, count), i => { Interlocked.Add(ref visits[i], 1); });
    }

    [GpuDeviceFunction]
    private static int CountedLength(int[] calls, int count)
    {
        Interlocked.Add(ref calls[0], 1);
        return count;
    }
}
