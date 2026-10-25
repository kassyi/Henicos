// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct ForUnrollAccumulator
{
    public int Total;
}

/// <summary>
/// Kernels that place thread barriers or suspending device function calls inside <see cref="Gpu.ForUnroll(int, int, System.Action{int})"/>
/// (#559).
/// </summary>
public static partial class ForUnrollBarrierKernels
{
    [GpuDeviceFunction]
    public static int Neighbor(int[] buffer, int tid, int value)
    {
        buffer[tid] = value;
        Gpu.SyncThreads();
        int r = buffer[(tid + 1) % 4];
        Gpu.SyncThreads();
        return r;
    }

    // K1: Neighbor exchange across SyncThreads inside ForUnroll
    [GpuKernel]
    public static void ExchangeKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 3, iter =>
        {
            buffer[tid] = tid + 10 * iter;
            Gpu.SyncThreads();
            sum += buffer[(tid + 1) % 4];
            Gpu.SyncThreads();
        });
        output[tid] = sum;
    }

    // K2: Suspending device function call inside ForUnroll
    [GpuKernel]
    public static void SuspendingCallKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 3, iter =>
        {
            sum += Neighbor(buffer, tid, tid * iter);
        });
        output[tid] = sum;
    }

    // K3: Context-passing ForUnroll(ref ctx, ...) with barriers
    [GpuKernel]
    public static void RefContextKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        ForUnrollAccumulator acc = default;
        Gpu.ForUnroll(ref acc, 1, 3, (ref a, i) =>
        {
            buffer[tid] = i * tid;
            Gpu.SyncThreads();
            a.Total += buffer[(tid + 1) % 4];
            Gpu.SyncThreads();
        });
        output[tid] = acc.Total;
    }

    // K4: Zero-iteration ForUnroll whose body contains barriers
    [GpuKernel]
    public static void ZeroCountKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        Gpu.ForUnroll(0, 0, iter =>
        {
            buffer[tid] = tid + iter + 1;
            Gpu.SyncThreads();
            output[tid] = buffer[(tid + 1) % 4];
            Gpu.SyncThreads();
        });
    }

    // K5: Outer ForUnroll with barriers containing an inner barrier-free ForUnroll
    [GpuKernel]
    public static void NestedForUnrollKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 2, o =>
        {
            buffer[tid] = tid + 10 * o;
            Gpu.SyncThreads();
            sum += buffer[(tid + 1) % 4];
            Gpu.ForUnroll(0, 3, j =>
            {
                sum += o * 10 + j;
            });
            Gpu.SyncThreads();
        });
        output[tid] = sum;
    }

    // K6: Reduced shape of KernelFermatReduceMega (ternary source selection + GridStrideLoop + SyncThreads)
    [GpuKernel]
    public static void MegaReducedKernel(int[] input, int[] scratch, int[] temp, int n)
    {
        Gpu.ForUnroll(0, 3, iter =>
        {
            var src = iter == 0 ? input : temp;
            Gpu.GridStrideLoop(n, i =>
            {
                scratch[i] = src[(i + 1) % n] + 1;
            });
            Gpu.SyncThreads();
            Gpu.GridStrideLoop(n, i =>
            {
                temp[i] = scratch[i];
            });
            Gpu.SyncThreads();
        });
    }
}
