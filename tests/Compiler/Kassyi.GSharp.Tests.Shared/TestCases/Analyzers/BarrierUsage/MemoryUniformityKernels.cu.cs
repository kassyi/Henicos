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

    // A write through a ref local to an element writes that memory
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void RefLocalWriteKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        ref int first = ref buffer[0];
        first = ThreadIndex.X;
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Ref local write
        }

        Gpu.SyncThreads();
    }

    // A read through a ref local to an element reads that memory
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void RefLocalReadKernel(int[] buffer)
    {
        ref int first = ref buffer[0];
        buffer[ThreadIndex.X] = 1;
        if (first > 0)
        {
            Gpu.SyncThreads(); // Ref local read
        }

        Gpu.SyncThreads();
    }

    // A local that holds the array is the same memory, written or read through either name
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AliasWriteKernel(int[] buffer)
    {
        int[] alias = buffer;
        alias[ThreadIndex.X] = 1;
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Alias write
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AliasReadKernel(int[] buffer)
    {
        int[] alias = buffer;
        alias[ThreadIndex.X] = 1;
        if (alias[0] > 0)
        {
            Gpu.SyncThreads(); // Alias read
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SpanAliasKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = 1;
        System.ReadOnlySpan<int> view = buffer;
        if (view[0] > 0)
        {
            Gpu.SyncThreads(); // Span alias
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SharedAliasKernel(int[] buffer)
    {
        var a = new SharedMemory<int>(32);
        var b = a;
        b[ThreadIndex.X] = 1;
        if (a[0] > 0)
        {
            Gpu.SyncThreads(); // Shared alias
        }

        Gpu.SyncThreads();
    }

    // Gpu.SyncGrid(body) syncs the grid between the statements of the body, not before the first one
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SyncGridFirstStatementKernel(int[] buffer, int[] other)
    {
        buffer[ThreadIndex.X] = 1;
        Gpu.SyncGrid(() =>
        {
            if (buffer[0] > 0)
            {
                Gpu.SyncThreads(); // SyncGrid first statement
            }

            Gpu.SyncThreads();
            other[ThreadIndex.X] = 2;
        });
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SyncGridGapKernel(int[] buffer)
    {
        Gpu.SyncGrid(() =>
        {
            buffer[ThreadIndex.X] = 1;
            if (buffer[0] > 0)
            {
                Gpu.SyncThreads();
            }
        });
    }

    // A struct member writes the array of its receiver
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ReceiverKernel(MemoryPutSlots slots)
    {
        Gpu.SyncThreads();
        slots.Put(ThreadIndex.X);
        if (slots.Values[0] > 0)
        {
            Gpu.SyncThreads(); // Struct receiver
        }

        Gpu.SyncThreads();
    }

    // A callee writes the array that a span receiver of a BCL call reaches
    [GpuDeviceFunction]
    public static void FillAll(System.Span<int> data)
    {
        data.Fill(ThreadIndex.X);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SpanReceiverKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        FillAll(buffer);
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Span receiver
        }

        Gpu.SyncThreads();
    }

    // An array auto-property of a struct is memory too
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AutoPropertyKernel(MemoryPropertySlots slots)
    {
        Gpu.SyncThreads();
        slots.Values[ThreadIndex.X] = 1;
        if (slots.Values[0] > 0)
        {
            Gpu.SyncThreads(); // Auto-property
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static void FillProperty(MemoryPropertySlots slots)
    {
        slots.Values[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void AutoPropertyThroughCallKernel(MemoryPropertySlots slots)
    {
        Gpu.SyncThreads();
        FillProperty(slots);
        if (slots.Values[0] > 0)
        {
            Gpu.SyncThreads(); // Auto-property through call
        }

        Gpu.SyncThreads();
    }

    // A write through a device function that returns a reference to an element
    [GpuDeviceFunction]
    public static ref int At(int[] data, int index) => ref data[index];

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void RefReturnWriteKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        At(buffer, ThreadIndex.X) = 1;
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Ref return write
        }

        Gpu.SyncThreads();
    }

    // The callee writes its ref parameter and then waits: the element is the same within the block afterwards
    [GpuDeviceFunction]
    public static void IncrementAndSync(ref int value)
    {
        value++;
        Gpu.SyncThreads();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void RefWriteThenSyncKernel(int[] buffer)
    {
        IncrementAndSync(ref buffer[0]);
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads();
        }

        Gpu.SyncThreads();
    }

    // The callee waits first and then writes its ref parameter: the element is written after the barrier
    [GpuDeviceFunction]
    public static void SyncAndIncrement(ref int value)
    {
        Gpu.SyncThreads();
        value++;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SyncThenRefWriteKernel(int[] buffer)
    {
        SyncAndIncrement(ref buffer[0]);
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Ref write after the callee's barrier
        }

        Gpu.SyncThreads();
    }

    // A callee that only waits at a block barrier leaves the memory differing between blocks: a grid barrier still
    // diverges on it
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void BlockHelperThenGridKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = 1;
        WaitForBlock();
        if (buffer[0] > 0)
        {
            Gpu.SyncGrid(); // Grid after block helper
        }

        Gpu.SyncGrid();
    }

    // A callee writes the array of a struct nested in its struct parameter
    [GpuDeviceFunction]
    public static void FillNested(MemoryOuterSlots outer)
    {
        outer.Inner.Values[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void NestedFieldThroughCallKernel(MemoryOuterSlots outer)
    {
        Gpu.SyncThreads();
        FillNested(outer);
        if (outer.Inner.Values[0] > 0)
        {
            Gpu.SyncThreads(); // Nested field through call
        }

        Gpu.SyncThreads();
    }

    // An increment of an element writes it
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void IncrementKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        buffer[ThreadIndex.X % 2]++;
        if (buffer[0] > 0)
        {
            Gpu.SyncThreads(); // Increment
        }

        Gpu.SyncThreads();
    }

    // A deconstruction into an element writes it
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void DeconstructionKernel(int[] buffer)
    {
        Gpu.SyncThreads();
        int other;
        (buffer[0], other) = (ThreadIndex.X, 1);
        if (buffer[0] > other)
        {
            Gpu.SyncThreads(); // Deconstruction
        }

        Gpu.SyncThreads();
    }
}

public struct MemoryPutSlots
{
    public int[] Values;

    public void Put(int value)
    {
        Values[ThreadIndex.X] = value;
    }
}

public struct MemoryPropertySlots
{
    public int[] Values { get; set; }
}

public struct MemoryOuterSlots
{
    public MemorySlots Inner;
}
