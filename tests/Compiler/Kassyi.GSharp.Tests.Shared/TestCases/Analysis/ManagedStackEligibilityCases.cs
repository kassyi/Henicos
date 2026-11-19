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
