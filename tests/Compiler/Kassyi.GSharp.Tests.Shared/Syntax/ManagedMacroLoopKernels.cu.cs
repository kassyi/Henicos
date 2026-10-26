// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that call <c>Gpu.ForUnroll</c> and <c>Gpu.GridStrideLoop</c> (both overloads) without a barrier, so that the
/// loop macros reach managed execution as written (#489).
/// </summary>
public static partial class ManagedMacroLoopKernels
{
    [GpuKernel]
    public static void ForUnrollSumKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 4, i => { sum += i; });
        output[tid] = sum;
    }

    [GpuKernel]
    public static void ForUnrollExpressionBodyKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = tid;
        Gpu.ForUnroll(2, 3, i => sum += i);
        output[tid] = sum;
    }

    [GpuKernel]
    public static void ForUnrollRefContextKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        ForUnrollAccumulator acc = default;
        acc.Total = tid;
        Gpu.ForUnroll(ref acc, 1, 3, (ref a, i) => { a.Total += i; });
        output[tid] = acc.Total;
    }

    [GpuKernel]
    public static void GridStrideRefContextKernel(int[] output, int count)
    {
        ForUnrollAccumulator acc = default;
        Gpu.GridStrideLoop(ref acc, count, (ref a, i) =>
        {
            a.Total += i;
            output[i] = a.Total;
        });
    }

    [GpuKernel]
    public static void NestedForUnrollKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 3, outer =>
        {
            Gpu.ForUnroll(0, 2, inner => { sum += outer * 10 + inner; });
        });
        output[tid] = sum;
    }

    [GpuKernel]
    public static void ForUnrollAroundGridStrideKernel(int[] output, int count)
    {
        Gpu.ForUnroll(0, 3, round =>
        {
            Gpu.GridStrideLoop(count, i => { output[i] += round + 1; });
        });
    }

    [GpuKernel]
    public static void GridStrideAroundForUnrollKernel(int[] output, int count)
    {
        Gpu.GridStrideLoop(count, i =>
        {
            int sum = 0;
            Gpu.ForUnroll(0, 3, step => { sum += i * step; });
            output[i] = sum;
        });
    }

    [GpuKernel]
    public static void ForUnrollSameNameAsLocalKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int i = tid;
        int sum = 0;
        Gpu.ForUnroll(0, 3, i => { sum += i; });
        output[tid] = sum + i;
    }

    private const int Size = 3;

    // The start is a compound constant expression: 1 << 1 + Size would not be the same loop
    [GpuKernel]
    public static void ForUnrollCompoundStartKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(1 << 1, Size, i => { sum += i; });
        output[tid] = sum;
    }

    // The context lives across the barrier, so the ref ctx argument of the barrier-free ForUnroll is a context field
    [GpuKernel]
    public static void BarrierRefContextForUnrollKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        ForUnrollAccumulator acc = default;
        acc.Total = tid;
        buffer[tid] = tid;
        Gpu.SyncThreads();
        Gpu.ForUnroll(ref acc, 1, 3, (ref a, i) => { a.Total += i + buffer[(tid + 1) % 4]; });
        output[tid] = acc.Total;
    }

    [GpuKernel]
    public static void ForUnrollSharedMemoryKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 2, round =>
        {
            var smem = new SharedMemory<int>(4);
            smem[tid] = tid + round;
            sum += smem[tid];
        });
        output[tid] = sum;
    }

    // Device functions that never suspend are not rewritten for managed execution: the loop macros run as Core code
    [GpuDeviceFunction]
    public static int UnrolledSum(int offset)
    {
        int sum = offset;
        Gpu.ForUnroll(0, 4, i => { sum += i; });
        return sum;
    }

    [GpuDeviceFunction]
    public static int UnrolledSumWithContext(int offset)
    {
        ForUnrollAccumulator acc = default;
        acc.Total = offset;
        Gpu.ForUnroll(ref acc, 1, 3, (ref a, i) => { a.Total += i; });
        return acc.Total;
    }

    [GpuDeviceFunction]
    public static void FillByStride(int[] data, int count, int value)
    {
        Gpu.GridStrideLoop(count, i => { data[i] = value + i; });
    }

    [GpuDeviceFunction]
    public static void RunningSumByStride(int[] data, int count)
    {
        ForUnrollAccumulator acc = default;
        Gpu.GridStrideLoop(ref acc, count, (ref a, i) =>
        {
            a.Total += i;
            data[i] = a.Total;
        });
    }

    [GpuKernel]
    public static void DeviceForUnrollKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = UnrolledSum(tid);
    }

    [GpuKernel]
    public static void DeviceForUnrollContextKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = UnrolledSumWithContext(tid * 10);
    }

    [GpuKernel]
    public static void DeviceGridStrideKernel(int[] output, int count)
    {
        FillByStride(output, count, 100);
    }

    [GpuKernel]
    public static void DeviceGridStrideContextKernel(int[] output, int count)
    {
        RunningSumByStride(output, count);
    }

    // The outer local has the name of the loop variable; the loop must not take it over or hide it
    [GpuKernel]
    public static void GridStrideSameNameAsLocalKernel(int[] data, int[] seen)
    {
        int tid = ThreadIndex.X;
        int i = tid + 100;
        Gpu.GridStrideLoop(data.Length, i => { data[i] += 1; });
        seen[tid] = i;
    }

    [GpuKernel]
    public static void GridStrideContextSameNameAsLocalKernel(int[] data, int[] seen)
    {
        int tid = ThreadIndex.X;
        int i = tid + 100;
        ForUnrollAccumulator acc = default;
        Gpu.GridStrideLoop(ref acc, data.Length, (ref a, i) =>
        {
            a.Total += 1;
            data[i] += 1;
        });
        seen[tid] = i + acc.Total * 1000;
    }

    // Every element must be visited exactly once, however many blocks the grid has
    [GpuKernel]
    public static void GridStrideVisitCountKernel(int[] data, int count)
    {
        Gpu.GridStrideLoop(count, i => { data[i] += 1; });
    }

    [GpuDeviceFunction]
    public static void CountVisits(int[] data, int count)
    {
        Gpu.GridStrideLoop(count, i => { data[i] += 1; });
    }

    [GpuKernel]
    public static void DeviceGridStrideVisitCountKernel(int[] data, int count)
    {
        CountVisits(data, count);
    }

    // start + count does not fit in an int: the loop runs no iteration in any dialect
    [GpuKernel]
    public static void ForUnrollNearIntMaxKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int iterations = 0;
        Gpu.ForUnroll(2147483646, 4, i => { iterations += 1; });
        output[tid] = iterations;
    }
}
