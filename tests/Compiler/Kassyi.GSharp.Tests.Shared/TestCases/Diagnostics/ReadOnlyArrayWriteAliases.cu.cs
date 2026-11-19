using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct ReadOnlyAliasWriteHolder
{
    [GpuReadOnly] public uint[] Fixed;
    public uint[] Free;
}

public struct ReadOnlyAliasWriteOuter
{
    public ReadOnlyAliasWriteHolder Inner;
}

public struct ReadOnlyAliasDataHolder
{
    public uint[] Data;
}

public struct ReadOnlyAliasOuter
{
    public ReadOnlyAliasDataHolder Inner;
}

public struct ReadOnlyAliasHolderWithOther
{
    public uint[] Data;
    public int Other;
}

public struct ReadOnlyAliasOuterWithOther
{
    public ReadOnlyAliasHolderWithOther Inner;
}

public struct ReadOnlyAliasCtorHolder
{
    public uint[] Data;
    public int Other;

    public ReadOnlyAliasCtorHolder(uint[] data)
    {
        Data = data;
        Other = 0;
    }
}

public struct ReadOnlyAliasCtorOuter
{
    public ReadOnlyAliasCtorHolder Inner;
    public int Tag;

    public ReadOnlyAliasCtorOuter(uint[] data)
    {
        Inner = new ReadOnlyAliasCtorHolder(data);
        Tag = 0;
    }
}

public struct ReadOnlyAliasExplicitDefaultCtorHolder
{
    public uint[] Data;
    public int Tag;

    public ReadOnlyAliasExplicitDefaultCtorHolder()
    {
        Data = default;
        Tag = 1;
    }
}

[SkipSnapshotConsistency("Diagnostics case")]
public static partial class ReadOnlyArrayWriteAliasKernels
{
    [GpuKernel]
    public static void NestedStructAliasKernel([GpuReadOnly] uint[] r)
    {
        var assigned = new ReadOnlyAliasOuter();
        assigned.Inner.Data = r;
        assigned.Inner.Data[0] = 4u;
        var initialized = new ReadOnlyAliasOuter { Inner = new ReadOnlyAliasDataHolder { Data = r } };
        initialized.Inner.Data[1] = 12u;
    }

    [GpuKernel]
    public static void TupleDeconstructionAliasKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var (p, q) = (r, o);
        p[0] = 1u;
        q[0] = 2u;
        (uint[] p2, var q2) = (r, o);
        p2[0] = 3u;
        var (a, (b, c)) = (o, (r, o));
        b[0] = 4u;
    }

    [GpuKernel]
    public static void ReassignedNestedStructKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyAliasOuter();
        w.Inner.Data = r;
        w.Inner = new ReadOnlyAliasDataHolder();
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void SwapWritableTupleKernel(uint[] a, uint[] b)
    {
        (a, b) = (b, a);
        a[0] = 1u;
    }

    [GpuKernel]
    public static void SelfAssignedFieldKernel()
    {
        var w = new ReadOnlyAliasDataHolder();
        w.Data = w.Data;
        w.Data[0] = 1u;
    }

    [GpuKernel]
    public static void InvalidateParentStructSiblingInitKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyAliasOuterWithOther();
        w.Inner.Data = r;
        w.Inner = new ReadOnlyAliasHolderWithOther { Other = 5 };
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void InvalidateParentStructEmptyInitKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyAliasOuter();
        w.Inner.Data = r;
        w.Inner = new ReadOnlyAliasDataHolder { };
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void TupleSwapWritableParamToLocalKernel([GpuReadOnly] uint[] r, uint[] b)
    {
        uint[] a;
        (a, b) = (b, r);
        a[0] = 1u;
    }

    [GpuKernel]
    public static void TupleSwapLocalReadOnlyWithWritableParamKernel([GpuReadOnly] uint[] r, uint[] b)
    {
        var a = r;
        (a, b) = (b, a);
        a[0] = 1u;
    }

    [GpuKernel]
    public static void NestedTupleSwapWritableParamsKernel([GpuReadOnly] uint[] r, uint[] b, uint[] c)
    {
        var a = r;
        (a, (b, c)) = (b, (c, a));
        a[0] = 1u;
    }

    [GpuKernel]
    public static void TupleSwapLocalReadOnlyWithParamFieldKernel([GpuReadOnly] uint[] r, ReadOnlyAliasOuter w)
    {
        var a = r;
        (a, w.Inner.Data) = (w.Inner.Data, a);
        a[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalParamFieldAssignKernel([GpuReadOnly] uint[] r, ReadOnlyAliasOuter w, bool cond)
    {
        if (cond)
        {
            w.Inner.Data = r;
        }

        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalParamFieldInitKernel([GpuReadOnly] uint[] r, ReadOnlyAliasOuter w, bool cond)
    {
        if (cond)
        {
            w.Inner = new ReadOnlyAliasDataHolder { Data = r };
        }

        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalArrayParamAssignKernel([GpuReadOnly] uint[] r, uint[] o, bool cond)
    {
        if (cond)
        {
            o = r;
        }

        o[0] = 1u;
    }

    [GpuKernel]
    public static void ReadParamBeforeOverwriteKernel([GpuReadOnly] uint[] r, uint[] b)
    {
        uint[] a = b;
        b = r;
        a[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalReadOnlyStructFieldToWritableKernel(
        ReadOnlyAliasWriteHolder h,
        uint[] o,
        bool cond)
    {
        if (cond)
        {
            h.Fixed = o;
        }

        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void LocalStructCopyFromParamConditionalAssignKernel(
        [GpuReadOnly] uint[] r,
        ReadOnlyAliasOuter w,
        bool cond)
    {
        var local = w;
        if (cond)
        {
            local.Inner.Data = r;
        }

        local.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ReadOnlyParamReassignedToReadOnlyKernel([GpuReadOnly] uint[] r, [GpuReadOnly] uint[] r2)
    {
        r = r2;
        r[0] = 1u;
    }

    [GpuKernel]
    public static void ReadOnlyStructFieldReassignedToReadOnlyKernel([GpuReadOnly] uint[] r, ReadOnlyAliasWriteHolder h)
    {
        h.Fixed = r;
        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void ExplicitCtorWithObjectInitializerKernel([GpuReadOnly] uint[] r, uint[] o, bool cond)
    {
        var emptyInit = new ReadOnlyAliasCtorHolder(o) { };
        if (cond)
        {
            emptyInit.Data = r;
        }

        emptyInit.Data[0] = 1u;

        var partialInit = new ReadOnlyAliasCtorHolder(o) { Other = 5 };
        if (cond)
        {
            partialInit.Data = r;
        }

        partialInit.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ExplicitParameterlessCtorWithObjectInitializerKernel([GpuReadOnly] uint[] r, bool cond)
    {
        var local = new ReadOnlyAliasExplicitDefaultCtorHolder { Tag = 2 };
        if (cond)
        {
            local.Data = r;
        }

        local.Data[0] = 1u;
    }

    [GpuKernel]
    public static void NestedExplicitCtorWithObjectInitializerKernel([GpuReadOnly] uint[] r, uint[] o, bool cond)
    {
        var outerCtor = new ReadOnlyAliasCtorOuter(o) { Tag = 1 };
        if (cond)
        {
            outerCtor.Inner.Data = r;
        }

        outerCtor.Inner.Data[0] = 1u;

        var nestedCtor = new ReadOnlyAliasCtorOuter { Inner = new ReadOnlyAliasCtorHolder(o) { Other = 5 } };
        if (cond)
        {
            nestedCtor.Inner.Data = r;
        }

        nestedCtor.Inner.Data[0] = 1u;

        var bareNested = new ReadOnlyAliasCtorOuter(o) { Inner = { Other = 5 } };
        if (cond)
        {
            bareNested.Inner.Data = r;
        }

        bareNested.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ExplicitCtorWithObjectInitializerReadOnlyFieldKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var local = new ReadOnlyAliasCtorHolder(o) { Data = r };
        local.Data[0] = 1u;
    }

    [GpuKernel]
    public static void LocalReadOnlyFieldStructCopyReassignedWritableKernel(
        [GpuReadOnly] uint[] r,
        ReadOnlyAliasWriteHolder h,
        uint[] o,
        bool cond)
    {
        var conditionalCopy = h;
        if (cond)
        {
            conditionalCopy.Fixed = o;
        }

        conditionalCopy.Fixed[0] = 1u;

        var unconditionalCopy = h;
        unconditionalCopy.Fixed = o;
        unconditionalCopy.Fixed[0] = 1u;

        var writableOther = new ReadOnlyAliasWriteHolder { Fixed = o };
        var writableCopy = writableOther;
        writableCopy.Fixed[0] = 1u;

        var defaultLocal = new ReadOnlyAliasWriteHolder { Free = o };
        defaultLocal.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void ReinitializedReadOnlyFieldStructKernel(
        [GpuReadOnly] uint[] r,
        ReadOnlyAliasWriteHolder h,
        uint[] o)
    {
        h = new ReadOnlyAliasWriteHolder { Free = o };
        h.Fixed[0] = 1u;

        var localReinit = new ReadOnlyAliasWriteHolder { Fixed = r };
        localReinit = new ReadOnlyAliasWriteHolder { Free = o };
        localReinit.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void OutVarDeclarationConditionalReadOnlyKernel([GpuReadOnly] uint[] r, uint[] o, bool cond)
    {
        GetArray(o, out var array);
        if (cond)
        {
            array = r;
        }

        array[0] = 1u;

        GetOuter(o, out var outer);
        if (cond)
        {
            outer.Inner.Data = r;
        }

        outer.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void OutReadOnlyParamReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        GetArray(o, out r);
        r[0] = 1u;
    }

    [GpuKernel]
    public static void ParenthesizedMemberReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyAliasOuter();
        w.Inner.Data = r;
        (w.Inner).Data = o;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ParenthesizedMemberAssignedReadOnlyKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyAliasOuter();
        (w.Inner).Data = r;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void CopiedReadOnlyFieldStructParamKernel(ReadOnlyAliasWriteHolder h)
    {
        var local = h;
        local.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void CopiedLocalReadOnlyFieldStructKernel([GpuReadOnly] uint[] r)
    {
        var first = new ReadOnlyAliasWriteHolder { Fixed = r };
        var copy = first;
        copy.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void CopiedNestedReadOnlyFieldStructKernel(ReadOnlyAliasWriteOuter h)
    {
        var inner = h.Inner;
        inner.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void DeconstructedReadOnlyFieldStructCopyKernel(ReadOnlyAliasWriteHolder h)
    {
        var (a, b) = (h, 0);
        a.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void ChainedCopiedReadOnlyFieldStructKernel(ReadOnlyAliasWriteHolder h)
    {
        var a = h;
        var b = a;
        b.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void CopiedWritableStructFieldKernel(uint[] o)
    {
        var other = new ReadOnlyAliasWriteHolder { Fixed = o };
        var local = other;
        local.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void CopiedStructOverwrittenFieldKernel(ReadOnlyAliasWriteHolder h, uint[] o, bool cond)
    {
        var unconditional = h;
        unconditional.Fixed = o;
        unconditional.Fixed[0] = 1u;

        var conditional = h;
        if (cond)
        {
            conditional.Fixed = o;
        }

        conditional.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void DefaultConstructedStructWithOmittedReadOnlyFieldKernel(uint[] o)
    {
        var omitted = new ReadOnlyAliasWriteHolder { Free = o };
        omitted.Fixed[0] = 1u;

        var copiedOmitted = omitted;
        copiedOmitted.Fixed[0] = 1u;

        var empty = new ReadOnlyAliasWriteHolder();
        empty.Fixed[0] = 1u;
    }

    [GpuDeviceFunction]
    private static void GetArray(uint[] source, out uint[] destination) => destination = source;

    [GpuDeviceFunction]
    private static void GetOuter(uint[] source, out ReadOnlyAliasOuter destination)
    {
        destination = new ReadOnlyAliasOuter();
        destination.Inner.Data = source;
    }
}
