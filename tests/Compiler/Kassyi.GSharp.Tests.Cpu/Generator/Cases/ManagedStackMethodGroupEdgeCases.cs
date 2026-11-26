using System;
using Kassyi.GSharp.Core;

namespace ManagedStackMethodGroupEdgeCases;

public static partial class MethodGroupEdgeKernels
{
    [GpuDeviceFunction]
    private static void ShuffleStep(int i)
    {
        Gpu.SyncThreads();
        uint mask = Warp.ActiveMask();
        _ = Warp.ShuffleXorSync(mask, i, 1);
    }

    [GpuDeviceFunction]
    private static void SelfGroup(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, SelfGroup);
    }

    [GpuKernel]
    public static void PassesSelfGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, SelfGroup);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static void GenericStep<T>(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, ShuffleStep);
    }

    [GpuKernel]
    public static void PassesGenericGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, GenericStep<int>);
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    private static partial void PartialStep(int i);

    private static partial void PartialStep(int i)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, ShuffleStep);
    }

    [GpuKernel]
    public static void PassesPartialGroupKernel(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.ForUnroll(0, 2, PartialStep);
        data[ThreadIndex.X] += 1;
    }
}

public static partial class UserNameofMethodGroupKernels
{
    private static void nameof(Action<int> step) => step(0);

    [GpuDeviceFunction]
    private static void ShuffleStep(int i)
    {
        Gpu.SyncThreads();
        uint mask = Warp.ActiveMask();
        _ = Warp.ShuffleXorSync(mask, i, 1);
    }

    [GpuKernel]
    public static void CallsUserNameofKernel(int[] data)
    {
        Gpu.SyncThreads();
        nameof(ShuffleStep);
        data[ThreadIndex.X] += 1;
    }
}
