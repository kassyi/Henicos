using System;
using Kassyi.GSharp.Core;

namespace ManagedStackEligibilityCases;

public ref struct CustomRefBuffer
{
    public int Value;
}

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class EligibilityKernels
{
    [GpuKernel]
    public static void BlockBarrierOnly(int[] data)
    {
        data[ThreadIndex.X] = 1;
        Gpu.SyncThreads();
        data[ThreadIndex.X] += 2;
    }

    [GpuKernel]
    public static void WithWarpShuffle(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        data[tid] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[tid], 1);
    }

    [GpuKernel]
    public static void WithActiveMask(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        data[tid] = (int)Warp.ActiveMask();
    }

    [GpuKernel]
    public static void CallsActiveMaskReachingDeviceFunction(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid % 2 == 0)
        {
            data[tid] = ActiveMaskDeviceWait(data[tid]);
        }
    }

    [GpuDeviceFunction]
    public static int ActiveMaskDeviceWait(int value)
    {
        if (value > 0)
        {
            value += (int)Warp.ActiveMask();
        }

        return value;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.SyncGrid();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void WithoutBarrier(int[] data)
    {
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void NonSpanRefStructParameter(int[] data, CustomRefBuffer custom)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = custom.Value;
    }

    [GpuKernel]
    public static void SpanParameterWithBarrier(Span<int> data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void CallsStackEligibleDeviceFunction(int[] data)
    {
        data[ThreadIndex.X] = StackDeviceWait(data[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void CallsShuffleReachingDeviceFunction(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = ShuffleDeviceWait(data[ThreadIndex.X]);
    }

    [GpuDeviceFunction]
    public static int StackDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return value + 1;
    }

    [GpuDeviceFunction]
    public static int ShuffleDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
    }

    [GpuKernel]
    public static void CallsStackExtension(int[] data)
    {
        data[ThreadIndex.X] = data.WaitAndAdd(1);
    }

    [GpuKernel]
    public static void PassesStackExtensionGroup(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, data.WaitAt);
    }

    [GpuDeviceFunction]
    public static int WaitAndAdd(this int[] data, int value)
    {
        Gpu.SyncThreads();
        return data[0] + value;
    }

    [GpuDeviceFunction]
    public static void WaitAt(this int[] data, int i)
    {
        Gpu.SyncThreads();
        data[i] += 1;
    }
}

[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public partial class InstanceEligibilityTarget
{
    [GpuKernel]
    public void InstanceKernelWithBarrier(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void CallsInstanceDeviceFunction(int[] data)
    {
        Gpu.SyncThreads();
        var target = new InstanceEligibilityTarget();
        data[ThreadIndex.X] = target.InstanceDeviceWait(data[ThreadIndex.X]);
    }

    [GpuDeviceFunction]
    public int InstanceDeviceWait(int value)
    {
        Gpu.SyncThreads();
        return value + 1;
    }
}

// Warp collectives on the managed stack model: which kernels run them on their stacks, and the limit of the visit
// counts of a GPU thread (WarpFrameLayout.MaxSize = 4096). The Chain functions double the counts of the one before:
// Chain0 takes 2, ChainN takes 1 + 2 * Chain(N - 1), so Chain10 takes 3071
[SkipSnapshotConsistency("Input for ManagedStackEligibilityTests")]
public static partial class WarpEligibilityKernels
{
    [GpuKernel]
    public static void WarpOnly(int[] data)
    {
        data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
    }

    [GpuKernel]
    public static void CallsRecursiveShuffle(int[] data)
    {
        data[ThreadIndex.X] = RecursiveShuffle(data[ThreadIndex.X], 2);
    }

    [GpuDeviceFunction]
    public static int RecursiveShuffle(int value, int depth) =>
        depth == 0 ? Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1) : RecursiveShuffle(value, depth - 1);

    [GpuKernel]
    public static void PassesShuffleGroup(int[] data)
    {
        Gpu.SyncWarp();
        Gpu.ForUnroll(0, 2, data.ShuffleAt);
    }

    [GpuDeviceFunction]
    public static void ShuffleAt(this int[] data, int i)
    {
        data[i] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[i], 1);
    }

    // The kernel reaches Warp.ActiveMask() itself, so its stack form writes the body lowered for the stacks
    [GpuKernel]
    public static void PassesActiveMaskGroup(int[] data)
    {
        if (ThreadIndex.X % 2 == 0)
        {
            data[ThreadIndex.X] = (int)Warp.ActiveMask();
        }

        Gpu.ForUnroll(0, 2, data.ActiveMaskAt);
    }

    [GpuDeviceFunction]
    public static void ActiveMaskAt(this int[] data, int i)
    {
        data[i] += (int)Warp.ActiveMask();
    }

    // 1 + 1 + 3071 + 767 + 191 + 47 + 11 + 5 + 2 = 4096 visit counts
    [GpuKernel]
    public static void AtTheVisitLimit(int[] data)
    {
        int v = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
        data[ThreadIndex.X] = Chain10(v) + Chain8(v) + Chain6(v) + Chain4(v) + Chain2(v) + Chain1(v) + Chain0(v);
    }

    // One collective more: 4097 visit counts
    [GpuKernel]
    public static void OverTheVisitLimit(int[] data)
    {
        int v = Warp.ShuffleXorSync(0xFFFFFFFFu, data[ThreadIndex.X], 1);
        v = Warp.ShuffleXorSync(0xFFFFFFFFu, v, 2);
        data[ThreadIndex.X] = Chain10(v) + Chain8(v) + Chain6(v) + Chain4(v) + Chain2(v) + Chain1(v) + Chain0(v);
    }

    [GpuDeviceFunction]
    public static int Chain0(int v) => Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

    [GpuDeviceFunction]
    public static int Chain1(int v) => Chain0(v) + Chain0(v);

    [GpuDeviceFunction]
    public static int Chain2(int v) => Chain1(v) + Chain1(v);

    [GpuDeviceFunction]
    public static int Chain3(int v) => Chain2(v) + Chain2(v);

    [GpuDeviceFunction]
    public static int Chain4(int v) => Chain3(v) + Chain3(v);

    [GpuDeviceFunction]
    public static int Chain5(int v) => Chain4(v) + Chain4(v);

    [GpuDeviceFunction]
    public static int Chain6(int v) => Chain5(v) + Chain5(v);

    [GpuDeviceFunction]
    public static int Chain7(int v) => Chain6(v) + Chain6(v);

    [GpuDeviceFunction]
    public static int Chain8(int v) => Chain7(v) + Chain7(v);

    [GpuDeviceFunction]
    public static int Chain9(int v) => Chain8(v) + Chain8(v);

    [GpuDeviceFunction]
    public static int Chain10(int v) => Chain9(v) + Chain9(v);
}
