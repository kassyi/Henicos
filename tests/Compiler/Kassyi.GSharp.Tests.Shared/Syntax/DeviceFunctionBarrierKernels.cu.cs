// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Kernels whose barriers live inside device functions, so that every thread must suspend inside the callee.</summary>
public static partial class DeviceFunctionBarrierKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    // --- Device functions that reach a barrier ---

    [GpuDeviceFunction]
    public static int ReverseThroughSharedMemory(int value, int length)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        shared[tid] = value;
        Gpu.SyncThreads();
        int reversed = shared[length - 1 - tid];
        Gpu.SyncThreads(); // Every read finishes before a later call overwrites the buffer
        return reversed;
    }

    [GpuDeviceFunction]
    public static int RotateThroughSharedMemory(int value, int length)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        shared[tid] = value;
        Gpu.SyncThreads();
        int rotated = shared[(tid + 1) % length];
        Gpu.SyncThreads();
        return rotated;
    }

    [GpuDeviceFunction]
    public static int WarpInclusiveScan(int value)
    {
        uint lane = (uint)ThreadIndex.X & 31u;
        for (uint offset = 1u; offset < 32u; offset <<= 1)
        {
            int neighbor = Warp.ShuffleUpSync(FullMask, value, offset);
            if (lane >= offset)
            {
                value += neighbor;
            }
        }

        return value;
    }

    // Calls another barrier-reaching device function; 'bias' lives across that call
    [GpuDeviceFunction]
    public static int ScaledReverse(int value, int length, int scale)
    {
        int bias = ThreadIndex.X * scale;
        int reversed = ReverseThroughSharedMemory(value, length);
        return reversed + bias;
    }

    [GpuDeviceFunction]
    public static int ExchangeThroughSharedParameter(SharedMemory<int> shared, int value, int length)
    {
        int tid = ThreadIndex.X;
        shared[tid] = value;
        Gpu.SyncThreads();
        return shared[length - 1 - tid];
    }

    [GpuDeviceFunction]
    public static void ReverseWithOriginal(int value, int length, out int reversed, out int original)
    {
        original = value;
        reversed = ReverseThroughSharedMemory(value, length);
    }

    // Warp-cooperative in-place reversal of a 32-element span, the pattern used by the stdlib warp kernels
    [GpuDeviceFunction]
    public static void ReverseSpanWarp(Span<int> data)
    {
        int lane = ThreadIndex.X & 31;
        int value = data[lane];
        int mirrored = Warp.ShuffleSync(FullMask, value, 31 - lane);
        data[lane] = mirrored;
    }

    // --- Helpers without barriers ---

    [GpuDeviceFunction]
    public static int TakeTicket(int[] tickets, int index)
    {
        tickets[index] += 1;
        return 0;
    }

    // --- Kernels ---

    [GpuKernel]
    public static void SharedMemoryReverseKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        output[tid] = ReverseThroughSharedMemory(input[tid], length);
    }

    [GpuKernel]
    public static void WarpInclusiveScanKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = WarpInclusiveScan(input[tid]);
    }

    // Control: the same scan written in the kernel body, which the CPU state machine already lowers
    [GpuKernel]
    public static void WarpInclusiveScanInKernelBodyKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        uint lane = (uint)tid & 31u;
        int value = input[tid];
        for (uint offset = 1u; offset < 32u; offset <<= 1)
        {
            int neighbor = Warp.ShuffleUpSync(FullMask, value, offset);
            if (lane >= offset)
            {
                value += neighbor;
            }
        }

        output[tid] = value;
    }

    [GpuKernel]
    public static void NestedBarrierCallKernel(int[] input, int[] output, int length, int scale)
    {
        int tid = ThreadIndex.X;
        output[tid] = ScaledReverse(input[tid], length, scale);
    }

    [GpuKernel]
    public static void RepeatedRotateKernel(int[] data, int length, int rounds)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        for (int round = 0; round < rounds; round++)
        {
            value = RotateThroughSharedMemory(value, length);
        }

        data[tid] = value;
    }

    // The argument has a side effect and the result feeds an expression: resuming must not evaluate the argument again
    [GpuKernel]
    public static void ArgumentEvaluatedOnceKernel(int[] input, int[] output, int[] tickets, int length)
    {
        int tid = ThreadIndex.X;
        output[tid] = ReverseThroughSharedMemory(input[tid] + TakeTicket(tickets, tid), length) + 1;
    }

    [GpuKernel]
    public static void SharedMemoryParameterKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> shared = new(256);
        output[tid] = ExchangeThroughSharedParameter(shared, input[tid], length);
    }

    [GpuKernel]
    public static void OutParameterKernel(int[] input, int[] reversedOutput, int[] originalOutput, int length)
    {
        int tid = ThreadIndex.X;
        ReverseWithOriginal(input[tid], length, out int reversed, out int original);
        reversedOutput[tid] = reversed;
        originalOutput[tid] = original;
    }

    // Mirrors the stdlib warp kernels: surplus threads return early, a uniform branch selects the call, and an array
    // converts to the Span parameter
    [GpuKernel]
    public static void UniformBranchSpanKernel(int[] primary, int[] alternate, int usePrimary)
    {
        if (ThreadIndex.X >= 32)
        {
            return;
        }

        if (usePrimary != 0)
        {
            ReverseSpanWarp(primary);
        }
        else
        {
            ReverseSpanWarp(alternate);
        }
    }
}
