using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public struct ValueFieldCell
{
    public int F;
}

public struct ValueFieldOuter
{
    public ValueFieldCell Inner;
    public int F;

    // ref this.F and ref this.Inner.F name fields of the struct itself: variables, no GSH0062
    [GpuDeviceFunction]
    public void AddToOwnFields()
    {
        ValueFieldInArgumentKernels.AddAfterBarrier(ref this.F, 1);
        ValueFieldInArgumentKernels.AddAfterBarrier(ref this.Inner.F, 1);
        ValueFieldInArgumentKernels.AddAfterBarrier(ref (this).F, 1);
    }
}

public static partial class ValueFieldInArgumentKernels
{
    [GpuDeviceFunction]
    public static ValueFieldCell MakeCell(int f)
    {
        ValueFieldCell c;
        c.F = f;
        return c;
    }

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

    // A field of a value (a call that returns a copy, an assignment, a conditional, a creation) is a value in C#, so
    // passing it to an in parameter without the keyword passes a copy: no GSH0062
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void ValueFieldInArgumentKernel(int[] result, int flag)
    {
        int j = ThreadIndex.X;
        ValueFieldCell a = MakeCell(1);
        ValueFieldCell b = MakeCell(2);
        ReadInAfterBarrier(MakeCell(j++).F, result);
        ReadInAfterBarrier((a = b).F, result);
        ReadInAfterBarrier((flag != 0 ? a : b).F, result);
        ReadInAfterBarrier(new ValueFieldCell { F = j++ }.F, result);
    }
}
