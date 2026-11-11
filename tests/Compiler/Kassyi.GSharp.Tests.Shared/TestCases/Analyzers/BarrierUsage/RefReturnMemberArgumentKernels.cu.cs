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
        // GSH0062: a ref conditional selects its variable again on every resume, also without the in keyword and in
        // parentheses (#927 is to keep the reference instead)
        bool first = j == 0;
        int v = 1;
        int w = 2;
        ReadInAfterBarrier(first ? ref v : ref w, result);
        ReadInAfterBarrier((first ? ref v : ref w), result);
        AddAfterBarrier(ref first ? ref v : ref w, 1);
        // An element of an inline array local and a parenthesized local stay variables
        RefReturnBuffer inline = default;
        ReadInAfterBarrier(inline[1], result);
        ReadInAfterBarrier((v), result);
        // GSH0062: an element of the inline array that a call returns by reference, with and without the keyword
        ReadInAfterBarrier(PickBuffer(ref inline)[0], result);
        ReadInAfterBarrier(in PickBuffer(ref inline)[0], result);
    }

    [GpuDeviceFunction]
    public static ref RefReturnBuffer PickBuffer(ref RefReturnBuffer buffer) => ref buffer;

    // A pointer is evaluated once, so what it points to stays a variable the lowering keeps
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static unsafe void PointerArgumentKernel(RefReturnCell* cells, int* values, int[] result)
    {
        ReadInAfterBarrier(*values, result);
        ReadInAfterBarrier(values[1], result);
        ReadInAfterBarrier(cells->F, result);
        ReadInAfterBarrier((*cells).F, result);
        AddAfterBarrier(ref *values, 1);
    }
}

[System.Runtime.CompilerServices.InlineArray(4)]
public struct RefReturnBuffer
{
    private int _element;
}
