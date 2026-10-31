using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct RefReturnCell
{
    public int F;
}

public static partial class RefReturnMemberArgumentKernels
{
    [GpuDeviceFunction]
    public static ref RefReturnCell SelectCell(RefReturnCell[] cells, int index) => ref cells[index];

    [GpuDeviceFunction]
    public static ref int Select(int[] buffer, int index) => ref buffer[index];

    [GpuDeviceFunction]
    public static void AddAfterBarrier(ref int target, int value)
    {
        Gpu.SyncThreads();
        target += value;
    }

    [GpuDeviceFunction]
    public static void ReadInAfterBarrier(in int value, int[] result)
    {
        Gpu.SyncThreads();
        result[ThreadIndex.X] = value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void RefReturnMemberArgumentKernel(RefReturnCell[] cells, int[] buffer, int[] result)
    {
        int j = ThreadIndex.X;
        // GSH0062: a member of the result of a call, through a member access and through parentheses
        AddAfterBarrier(ref SelectCell(cells, j).F, j++);
        AddAfterBarrier(ref (SelectCell(cells, j)).F, 1);
        // GSH0062: an in parameter takes the reference that the call returns, without the in keyword
        ReadInAfterBarrier(Select(buffer, j), result);
        // A member of an element and a field of a local stay variables
        RefReturnCell local = cells[0];
        AddAfterBarrier(ref cells[j].F, 1);
        AddAfterBarrier(ref local.F, 1);
        ReadInAfterBarrier(buffer[j], result);
    }
}
