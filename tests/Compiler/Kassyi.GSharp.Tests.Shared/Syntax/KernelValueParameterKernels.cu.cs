// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct KernelParameterPoint
{
    public int X;
    public int Y;
}

public struct KernelParameterHolder
{
    public int[] Data;
}

/// <summary>
/// Kernels that write into a value parameter before a barrier and read it after (#879). The CPU paths run a kernel as a
/// state machine and pass the original arguments again on every resume, so a parameter the kernel writes must live in
/// its context.
/// </summary>
public static partial class KernelValueParameterKernels
{
    [GpuKernel]
    public static void ScalarParameterKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        value = value + tid;
        Gpu.SyncThreads();
        output[tid] = value;
    }

    [GpuKernel]
    public static void ArrayParameterKernel(int[] values, int[] replacement, int[] output)
    {
        int tid = ThreadIndex.X;
        values = replacement;
        Gpu.SyncThreads();
        output[tid] = values.Length * 1000 + values[tid];
    }

    // A read-only array parameter keeps its element constness in the context
    [GpuKernel]
    public static void ReadOnlyArrayParameterKernel([GpuReadOnly] int[] values, [GpuReadOnly] int[] replacement,
        int[] output)
    {
        int tid = ThreadIndex.X;
        values = replacement;
        Gpu.SyncThreads();
        output[tid] = values.Length * 1000 + values[tid];
    }

    // The shape of the report: the array comes from a field of a struct parameter
    [GpuKernel]
    public static void ArrayFromStructFieldKernel(KernelParameterHolder holder, int[] values, int[] output)
    {
        int tid = ThreadIndex.X;
        values = holder.Data;
        Gpu.SyncThreads();
        output[tid] = values.Length * 1000 + values[tid];
    }

    [GpuKernel]
    public static void StructParameterKernel(KernelParameterPoint point, KernelParameterPoint replacement, int[] output)
    {
        int tid = ThreadIndex.X;
        point = replacement;
        point.Y = point.Y + tid;
        Gpu.SyncThreads();
        output[tid] = point.X * 1000 + point.Y;
    }

    [GpuKernel]
    public static void StructFieldParameterKernel(KernelParameterPoint point, int[] output)
    {
        int tid = ThreadIndex.X;
        point.X = point.X + tid;
        Gpu.SyncThreads();
        output[tid] = point.X * 1000 + point.Y;
    }

    // Each barrier resumes the kernel once more; the parameter keeps every write
    [GpuKernel]
    public static void ParameterAcrossTwoBarriersKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        value += tid;
        Gpu.SyncThreads();
        value *= 10;
        Gpu.SyncThreads();
        output[tid] = value;
    }

    // The loop goes back over the barrier; every iteration reads the value the previous one wrote
    [GpuKernel]
    public static void ParameterInBarrierLoopKernel(int value, int[] output)
    {
        for (int i = 0; i < 3; i++)
        {
            value = value * 2 + i;
            Gpu.SyncThreads();
        }

        output[ThreadIndex.X] = value + ThreadIndex.X * 100;
    }

    // Without a barrier the kernel runs once; the written parameter needs no copy
    [GpuKernel]
    public static void ParameterWithoutBarrierKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        value = value + tid;
        output[tid] = value;
    }
}
