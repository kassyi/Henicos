using Kassyi.GSharp.Core;
#if GSHARP_BOTH_STACKS
using System;
using Kassyi.ZeroAlloc.Linq;
#endif

namespace Kassyi.GSharp.Tests.Shared.Differential;

// Locals that GSH0039 and GSH0041 reject on the state machine, kept across Gpu.SyncThreads() where both CPU paths run
// on stacks. Each kernel runs one block of 32 threads; after the barrier a thread reads what it or another thread wrote
// before it, and no two threads write one element between two barriers
[SkipSnapshotConsistency("Built only with both CPU paths on stacks, where the state machine would reject it")]
public static partial class StackLiftedLocalsDifferentialKernels
{
#if GSHARP_BOTH_STACKS
    public const int Threads = 32;

    // A span over the two elements of the thread, kept across the barrier, then a read of the neighbor's second element
    [GpuKernel]
    public static void SpanAcrossBarrierKernel(int[] data, int[] output)
    {
        int t = ThreadIndex.X;
        Span<int> mine = data.AsSpan(t * 2, 2);
        mine[0] = t + 1;
        mine[1] = 3 * t;
        Gpu.SyncThreads();
        output[t] = (mine[0] * 100) + data[(((t + 1) & 31) * 2) + 1];
    }

    // A StackLinq chain made before the barrier and enumerated after it: it sees the writes of every thread
    [GpuKernel]
    public static void StackLinqChainAcrossBarrierKernel(int[] data, int[] output)
    {
        int t = ThreadIndex.X;
        data[t] = t - 10;
        var chain = data.AsStackEnumerable().Where(static x => x > 0);
        Gpu.SyncThreads();
        output[t] = chain.Sum() + t;
    }

    // A ref local to the element of the thread, written before, between and after two barriers
    [GpuKernel]
    public static void RefLocalAcrossBarrierKernel(int[] data, int[] output)
    {
        int t = ThreadIndex.X;
        ref int mine = ref data[t];
        mine = t * 5;
        Gpu.SyncThreads();
        int neighbor = data[(t + 1) & 31];
        Gpu.SyncThreads();
        mine += neighbor;
        output[t] = mine;
    }

    // A span parameter that the device function slices before the barrier and reads after it
    [GpuDeviceFunction]
    public static int SliceAcrossBarrier(Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        return s[0];
    }

    [GpuKernel]
    public static void SpanParameterAcrossBarrierKernel(int[] data, int[] output)
    {
        int t = ThreadIndex.X;
        data[t] = t * 7;
        if (t == 0)
        {
            data[Threads] = 1000;
        }

        output[t] = SliceAcrossBarrier(data.AsSpan(t));
    }

    // A ref foreach variable kept across the barrier inside the loop body
    [GpuKernel]
    public static void RefForEachAcrossBarrierKernel(int[] data)
    {
        int t = ThreadIndex.X;
        foreach (ref int element in data.AsSpan(t * 2, 2))
        {
            Gpu.SyncThreads();
            element += t;
        }
    }

    // A pointer to a local of the thread, written through after the barrier
    [GpuKernel]
    public static unsafe void LocalAddressAcrossBarrierKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int value = t * 3;
        int* pointer = &value;
        Gpu.SyncThreads();
        *pointer += 1;
        output[t] = value + *pointer;
    }

    // A stackalloc block of the thread, written before the barrier and read after it
    [GpuKernel]
    public static unsafe void StackallocAcrossBarrierKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int* block = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            block[i] = t + i;
        }

        Gpu.SyncThreads();
        output[t] = block[0] + block[1] + block[2] + block[3];
    }
#endif
}
