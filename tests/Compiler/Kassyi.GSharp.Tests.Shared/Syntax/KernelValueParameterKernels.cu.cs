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

public static class KernelParameterPointExtensions
{
    [GpuDeviceFunction]
    public static void Bump(this ref KernelParameterPoint point, int d)
    {
        point.X += d;
    }
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

    [GpuDeviceFunction]
    public static void AddTo(ref int target, int d)
    {
        target += d;
    }

    [GpuDeviceFunction]
    public static void SetTo(out int target, int value)
    {
        target = value;
    }

    // The receiver of a `this ref` extension method is written
    [GpuKernel]
    public static void RefExtensionReceiverKernel(KernelParameterPoint point, int[] output)
    {
        int tid = ThreadIndex.X;
        point.Bump(tid);
        Gpu.SyncThreads();
        output[tid] = point.X;
    }

    [GpuKernel]
    public static void RefArgumentAndIncrementKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        AddTo(ref value, tid);
        value++;
        Gpu.SyncThreads();
        output[tid] = value;
    }

    [GpuKernel]
    public static void OutArgumentKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        SetTo(out value, tid * 3);
        Gpu.SyncThreads();
        output[tid] = value;
    }

    [GpuKernel]
    public static void DeconstructionKernel(int value, int other, int[] output)
    {
        int tid = ThreadIndex.X;
        (value, other) = (value + tid, other + 1);
        Gpu.SyncThreads();
        output[tid] = value * 1000 + other;
    }

    // A local function that the kernel calls before the barrier writes the parameter it captures
    [GpuKernel]
    public static void LocalFunctionWriteKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;

        void Add(int d)
        {
            value += d;
        }

        Add(tid);
        Gpu.SyncThreads();
        output[tid] = value;
    }

    // Without a barrier the kernel runs once; the written parameter needs no copy
    [GpuKernel]
    public static void ParameterWithoutBarrierKernel(int value, int[] output)
    {
        int tid = ThreadIndex.X;
        value = value + tid;
        output[tid] = value;
    }

    // A write through a conditional ref expression before a barrier (#1000)
    [GpuKernel]
    public static void RefTernaryParameterKernel(int paramAssign, int paramCompound, int paramInc, int[] output)
    {
        int tid = ThreadIndex.X;
        int localFallback = -999;
        ((tid & 1) == 0 ? ref paramAssign : ref localFallback) = 777;
        ((tid & 2) == 0 ? ref paramCompound : ref localFallback) += 100;
        ++((tid & 4) == 0 ? ref paramInc : ref localFallback);
        Gpu.SyncThreads();
        output[tid] = paramAssign * 10000 + paramCompound * 10 + paramInc;
    }
}
