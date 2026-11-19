// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cpu.Core;

public struct NativeStackRecorder : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Marker;

    public void Dispose() => Log[Slot] = Marker;
}

[SkipSnapshotConsistency("Covered by NativeStackModelGeneratorTests; tests native stack execution")]
public static partial class NativeStackKernels
{
    [GpuDeviceFunction]
    public static int ReadElement(int[] source, int index) => source[(long)index];

    [GpuDeviceFunction]
    public static int ReadElementNested(int[] source, int index) => ReadElement(source, index);

    [GpuDeviceFunction]
    public static int ExchangeWithNeighbour(int[] scratch, int value)
    {
        int tid = ThreadIndex.X;
        scratch[tid] = value;
        Gpu.SyncThreads();
        return scratch[(tid + 1) % BlockDimension.X];
    }

    [GpuDeviceFunction]
    public static int OuterExchangeAndBias(int[] scratch, int value, int bias)
    {
        int neighbour = ExchangeWithNeighbour(scratch, value);
        Gpu.SyncThreads();
        scratch[ThreadIndex.X] = neighbour + bias;
        Gpu.SyncThreads();
        return scratch[(ThreadIndex.X + 2) % BlockDimension.X];
    }

    [GpuDeviceFunction]
    public static int ShuffleDeviceHelper(int x)
    {
        return Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1);
    }

    [GpuDeviceFunction]
    public static void SuspendingWithUsingAfterBarrier(int[] log, int marker)
    {
        int tid = ThreadIndex.X;
        log[tid] = 1;
        Gpu.SyncThreads();
        if (tid == 1)
        {
            using (var recorder = new NativeStackRecorder { Log = log, Slot = 4, Marker = marker })
            {
                log[5] = marker + 1;
            }
        }
    }

    [GpuKernel]
    public static void NeighbourKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        buffer[tid] = tid + 1;
        Gpu.SyncThreads();
        output[tid] = buffer[(tid + 1) % 4];
    }

    [GpuKernel]
    public static void MultiBlockNeighbourKernel(int[] buffer, int[] output)
    {
        int tid = ThreadIndex.X;
        int bid = BlockIndex.X;
        int blockOffset = bid * BlockDimension.X;
        buffer[blockOffset + tid] = (bid + 1) * 10000 + tid + 1;
        Gpu.SyncThreads();
        output[blockOffset + tid] = buffer[blockOffset + (tid + 1) % BlockDimension.X];
    }

    [GpuKernel]
    public static void LoopBarrierLocalsKernel(int[] scratch, int[] output)
    {
        int tid = ThreadIndex.X;
        int n = BlockDimension.X;
        int acc = tid + 1;
        int stepBias = tid * 7;
        for (int round = 0; round < 100; round++)
        {
            scratch[tid] = tid + round;
            Gpu.SyncThreads();
            acc += scratch[(tid + 1) % n] + stepBias;
            Gpu.SyncThreads();
        }

        output[tid] = acc;
    }

    [GpuKernel]
    public static void EarlyReturnBeforeBarrierKernel(int[] buffer, int[] output, int activeCount)
    {
        int tid = ThreadIndex.X;
        if (tid >= activeCount)
        {
            output[tid] = -1;
            return;
        }

        buffer[tid] = tid + 10;
        Gpu.SyncThreads();
        output[tid] = buffer[(tid + 1) % activeCount];
    }

    [GpuKernel]
    public static int ReturningBarrierKernel(int[] buffer)
    {
        int tid = ThreadIndex.X;
        buffer[tid] = tid + 10;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % BlockDimension.X] + tid;
    }

    [GpuKernel]
    public static void SharedMemoryBarrierKernel(int[] input, int[] output)
    {
        var shared = new SharedMemory<int>(8);
        int tid = ThreadIndex.X;
        shared[tid] = input[tid] * 2;
        Gpu.SyncThreads();
        output[tid] = shared[(tid + 3) % BlockDimension.X];
    }

    [GpuKernel]
    public static void FailureAfterBarrierKernel(int[] source, int[] before, int[] after, int failingThread, int badIndex)
    {
        int tid = ThreadIndex.X;
        before[tid] = tid + 100;
        Gpu.SyncThreads();
        int value = tid == failingThread ? ReadElementNested(source, badIndex) : ReadElementNested(source, 0);
        after[tid] = value + tid;
    }

    [GpuKernel]
    public static void TwoThreadsFailAfterBarrierKernel(int[] source, int[] after, int badIndex)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        int value = (tid == 1 || tid == 3) ? ReadElementNested(source, badIndex) : ReadElementNested(source, 0);
        after[tid] = value + tid;
    }

    [GpuKernel]
    public static void UsingUnwindAfterBarrierKernel(int[] source, int[] log, int badIndex)
    {
        int tid = ThreadIndex.X;
        log[tid] = 1;
        Gpu.SyncThreads();
        if (tid == 1)
        {
            using (var recorder = new NativeStackRecorder { Log = log, Slot = 4, Marker = 777 })
            {
                log[5] = ReadElementNested(source, badIndex);
                log[6] = 999;
            }
        }
    }

    [GpuKernel]
    public static void OneLevelSuspendingDeviceKernel(int[] scratch, int[] output)
    {
        int tid = ThreadIndex.X;
        int local = tid * 10;
        int neighbour = ExchangeWithNeighbour(scratch, tid + 1);
        output[tid] = local + neighbour;
    }

    [GpuKernel]
    public static void MultiLevelSuspendingDeviceKernel(int[] scratch, int[] output)
    {
        int tid = ThreadIndex.X;
        int result = OuterExchangeAndBias(scratch, tid + 1, 100);
        output[tid] = result;
    }

    [GpuKernel]
    public static void SuspendingDeviceUsingKernel(int[] log, int marker)
    {
        SuspendingWithUsingAfterBarrier(log, marker);
    }

    [GpuKernel]
    public static void ShuffleDeviceFallbackKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = ShuffleDeviceHelper(tid + 10);
    }

    // Kept in the same kernel class so generator snapshot tests verify that ineligible kernels stay state machines.
    [GpuKernel]
    public static void ShuffleAndBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int shuffled = Warp.ShuffleXorSync(0xFFFFFFFFu, tid, 1);
        Gpu.SyncThreads();
        output[tid] = shuffled;
    }
}
