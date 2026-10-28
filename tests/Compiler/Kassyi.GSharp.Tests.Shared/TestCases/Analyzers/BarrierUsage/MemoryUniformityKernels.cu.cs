using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct MemorySlots
{
    public int[] Values;
}

// C9 (noclobber): an element read with a uniform index is uniform only when no path from the last barrier writes the
// same memory; a block barrier makes it the same within a block, a grid barrier within the grid
public static partial class MemoryUniformityKernels
{
    [GpuDeviceFunction]
    public static void Store(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
    }

    // Written, then a barrier, then read: the same in every thread of the block
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WriteBarrierReadKernel(int[] buffer)
    {
        var slot = new SharedMemory<int>(32);
        slot[ThreadIndex.X] = buffer[ThreadIndex.X];
        Gpu.SyncThreads();
        if (slot[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // Written after the barrier: another thread may not have written it yet
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void WriteAfterBarrierKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        buffer[ThreadIndex.X] = 1;
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Write after barrier
        }

        Gpu.SyncThreads();
    }

    // A call that writes the memory, and an atomic
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CallAndAtomicKernel(int[] buffer, int[] counter)
    {
        Gpu.SyncThreads();
        Store(buffer, 1);
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Call
        }

        Gpu.AtomicAdd(ref counter[0], 1);
        if (counter[0] > 0)
        {
            Gpu.SyncThreads(); // Atomic
        }

        Gpu.SyncThreads();
    }

    // Never written in the kernel: the same in every thread
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void InputKernel(int[] input, int[] output)
    {
        output[ThreadIndex.X] = 1;
        if (input[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // After a block barrier the global memory is the same within a block, not yet within the grid
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BlockThenGridKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = 1;
        Gpu.SyncThreads();
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads();
        }

        if (buffer[0] > 0)
        {
            Gpu.SyncGrid(); // Grid after block
        }

        Gpu.SyncGrid();
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void GridBarrierKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = 1;
        Gpu.SyncGrid();
        if (buffer[0] > 0)
        {
            Gpu.SyncGrid();
        }

        Gpu.SyncGrid();
    }

    // Shared memory belongs to one block: never the same within the grid
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SharedAfterGridKernel(int[] buffer)
    {
        var slot = new SharedMemory<int>(32);
        slot[ThreadIndex.X] = 1;
        Gpu.SyncGrid();
        if (slot[0] > 0)
        {
            Gpu.SyncGrid(); // Shared
        }

        Gpu.SyncGrid();
    }

    // The callee writes and then waits at a barrier: the memory it leaves is the same within the block
    [GpuDeviceFunction]
    public static void StoreAndSync(int[] data, int value)
    {
        data[ThreadIndex.X] = value;
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void CallThatSyncsKernel(int[] buffer)
    {
        StoreAndSync(buffer, 1);
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // A callee writes the array of a struct argument; the caller reads it
    [GpuDeviceFunction]
    public static void FillSlots(MemorySlots slots, int value)
    {
        slots.Values[ThreadIndex.X] = value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void FieldThroughCallKernel(MemorySlots slots)
    {
        FillSlots(slots, ThreadIndex.X);
        if (slots.Values[0] == 0)
        {
            Gpu.SyncThreads(); // Field through call
        }

        Gpu.SyncThreads();
    }

    // A callee that waits at a barrier on every path before it returns: what the caller wrote before the call is the same
    // within the block afterwards
    [GpuDeviceFunction]
    public static void ReduceWithLeadingSync(SharedMemory<int> values)
    {
        Gpu.SyncThreads();
        for (int stride = 16; stride > 0; stride /= 2)
        {
            if (ThreadIndex.X < stride)
            {
                values[ThreadIndex.X] += values[ThreadIndex.X + stride];
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReduceKernel(int[] input)
    {
        var values = new SharedMemory<int>(32);
        values[ThreadIndex.X] = input[ThreadIndex.X];
        ReduceWithLeadingSync(values);
        if (values[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void WaitForBlock()
    {
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BarrierHelperKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = 1;
        WaitForBlock();
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // Some threads return before the barrier of the callee: the write is not known to be followed by one
    [GpuDeviceFunction]
    public static void SyncUnlessBeyond(int count)
    {
        if (ThreadIndex.X >= count)
        {
            return;
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void EarlyReturnCalleeKernel(int[] buffer, int count)
    {
        buffer[ThreadIndex.X] = 1;
        SyncUnlessBeyond(count); // Early return call
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Early return
        }

        Gpu.SyncThreads();
    }
}
