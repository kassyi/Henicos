using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0118 limits: cases where the alias analysis stays conservative and does not report a write
public static partial class ReadOnlyArrayWriteLimitKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeconstructCallKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        uint[] p;
        uint[] q;
        (p, q) = Pair(r, o);
        p[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeconstructCallReassignedKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var (a, b) = Pair(o, o);
        if (o[0] == 0u)
        {
            a = r;
        }

        a[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void StructCompoundAssignmentKernel([GpuReadOnly] uint[] r, ReadOnlyAliasOpHolder delta)
    {
        var w = new ReadOnlyAliasOpOuter();
        w.Inner.Data = r;
        w.Inner += delta;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TupleSwapReadOnlyIntoParamWriteKernel([GpuReadOnly] uint[] r, uint[] b)
    {
        uint[] a;
        (a, b) = (b, r);
        b[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TupleSwapLocalReadOnlyIntoParamFieldWriteKernel(
        [GpuReadOnly] uint[] r,
        ReadOnlyAliasLimitOuter w)
    {
        var a = r;
        (a, w.Inner.Data) = (w.Inner.Data, a);
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TupleSwapParamReceivesLocalReadOnlyWriteKernel([GpuReadOnly] uint[] r, uint[] b)
    {
        var a = r;
        (b, a) = (a, b);
        b[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedStructParamDirectAliasKernel([GpuReadOnly] uint[] r, ReadOnlyAliasLimitOuter w)
    {
        w.Inner.Data = r;
        w.Inner.Data[0] = 1u;
    }

    // A write through a ref local is not followed back to the variable it aliases
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RefLocalStructAliasWriteKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyAliasLimitOuter();
        w.Inner.Data = r;
        ref var i = ref w.Inner;
        i.Data[0] = 1u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RefLocalArrayAliasWriteKernel([GpuReadOnly] uint[] r)
    {
        var a = r;
        ref var d = ref a;
        d[0] = 1u;
    }

    [GpuDeviceFunction]
    private static (uint[], uint[]) Pair(uint[] first, uint[] second) => (first, second);
}

public struct ReadOnlyAliasLimitHolder
{
    public uint[] Data;
}

public struct ReadOnlyAliasLimitOuter
{
    public ReadOnlyAliasLimitHolder Inner;
}

public struct ReadOnlyAliasOpHolder
{
    public uint[] Data;
    public int Tag;

    public static ReadOnlyAliasOpHolder operator +(ReadOnlyAliasOpHolder left, ReadOnlyAliasOpHolder right) =>
        new() { Data = right.Data, Tag = left.Tag + right.Tag };
}

public struct ReadOnlyAliasOpOuter
{
    public ReadOnlyAliasOpHolder Inner;
}
