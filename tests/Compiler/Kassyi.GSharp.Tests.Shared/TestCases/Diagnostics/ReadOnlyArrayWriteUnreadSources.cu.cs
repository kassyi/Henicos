using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct ReadOnlyUnreadHolder
{
    [GpuReadOnly] public uint[] Fixed;
    public uint[] Free;
}

public struct ReadOnlyUnreadAttrInner
{
    [GpuReadOnly] public uint[] Data;
}

public struct ReadOnlyUnreadAttrOuter
{
    public ReadOnlyUnreadAttrInner Inner;
}

public struct ReadOnlyUnreadInner
{
    public uint[] Data;
}

public struct ReadOnlyUnreadOuter
{
    public ReadOnlyUnreadInner Inner;
}

[SkipSnapshotConsistency("Diagnostics case")]
public static partial class ReadOnlyArrayWriteUnreadSourceKernels
{
    [GpuKernel]
    public static void UnreadableStructArrayElementKernel(ReadOnlyUnreadHolder[] hs)
    {
        var w = hs[0];
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void UnreadableStructFromCallKernel(uint[] o)
    {
        var w = MakeHolder(o);
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void ReassignedStructParamFromCallKernel(ReadOnlyUnreadHolder h, uint[] o)
    {
        h = MakeHolder(o);
        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void NestedUnreadableStructArrayElementKernel(ReadOnlyUnreadAttrOuter[] ws)
    {
        var w = ws[0];
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ReinitializedStructThenReadOnlyKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadInner();
        w = new ReadOnlyUnreadInner();
        w.Data = r;
        w.Data[0] = 1u;
    }

    [GpuKernel]
    public static void DefaultAssignedStructThenReadOnlyKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadInner();
        w = default;
        w.Data = r;
        w.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ReinitializedNestedStructThenReadOnlyKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner = new ReadOnlyUnreadInner { };
        w.Inner.Data = r;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void OutStructReadOnlyFieldKernel(uint[] o)
    {
        GetHolder(o, out var h);
        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void OutReadOnlyFieldKernel(ReadOnlyUnreadHolder h, uint[] o)
    {
        GetArray(o, out h.Fixed);
        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void RefArgumentStructReadOnlyFieldKernel(ReadOnlyUnreadHolder[] hs)
    {
        var w = hs[0];
        Touch(ref w);
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalStructReadOnlyFieldKernel(ReadOnlyUnreadHolder a, ReadOnlyUnreadHolder b, bool c)
    {
        var w = c ? a : b;
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void DeconstructedUnreadStructKernel(ReadOnlyUnreadHolder[] hs, ReadOnlyUnreadHolder h)
    {
        var w = new ReadOnlyUnreadHolder();
        (w, h) = (hs[0], h);
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void UnreadStructWritableFieldKernel(ReadOnlyUnreadHolder[] hs)
    {
        var w = hs[0];
        w.Free[0] = 1u;
    }

    [GpuKernel]
    public static void UnreadableStructOverwrittenWritableFieldKernel(ReadOnlyUnreadHolder[] hs, uint[] o)
    {
        var w = hs[0];
        w.Fixed = o;
        w.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void UnreadableStructCopiedKernel(ReadOnlyUnreadHolder[] hs)
    {
        var w = hs[0];
        var copy = w;
        copy.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void NestedFieldUnreadableStructKernel(uint[] o)
    {
        var w = new ReadOnlyUnreadAttrOuter();
        w.Inner = MakeAttrInner(o);
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalNestedStructReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        ref var i = ref w.Inner;
        i.Data = o;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalNestedArrayFieldReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        ref uint[] d = ref w.Inner.Data;
        d = o;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalSingleLevelStructReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadInner();
        w.Data = r;
        ref var i = ref w;
        i.Data = o;
        w.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefReassignmentNestedStructReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        var other = new ReadOnlyUnreadInner();
        ref var i = ref other;
        i = ref (w.Inner);
        i.Data = o;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void ConditionalRefStructReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o, bool c)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        var v = new ReadOnlyUnreadOuter();
        ref var i = ref (c ? ref w.Inner : ref v.Inner);
        i.Data = o;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalArrayReassignedWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var a = r;
        ref var d = ref a;
        d = o;
        a[0] = 1u;
    }

    [GpuKernel]
    public static void RefReadOnlyLocalNestedStructWriteThroughOriginalKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        ref readonly var i = ref w.Inner;
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void RefReadOnlyLocalReadOnlyArrayWriteKernel([GpuReadOnly] uint[] r)
    {
        ref readonly uint[] d = ref r;
        d[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalStructWithReadOnlyFieldUnreassignedKernel(ReadOnlyUnreadHolder h)
    {
        ref var i = ref h;
        i.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void RefLocalStructWithReadOnlyFieldReassignedViaAliasKernel(ReadOnlyUnreadHolder h, uint[] o)
    {
        ref var i = ref h;
        i.Fixed = o;
        h.Fixed[0] = 1u;
    }

    [GpuKernel]
    public static void InArgumentKeepsSourcesKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        ReadIn(in w.Inner);
        w.Inner.Data[0] = 1u;
    }

    [GpuKernel]
    public static void LocalFunctionAssignsReadOnlyKernel([GpuReadOnly] uint[] r)
    {
        var w = new ReadOnlyUnreadOuter();
        Set();
        w.Inner.Data[0] = 1u;

        void Set() => w.Inner.Data = r;
    }

    [GpuKernel]
    public static void LocalFunctionAssignsWritableKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        Set(o);
        w.Inner.Data[0] = 1u;

        void Set(uint[] x) => w.Inner.Data = x;
    }

    [GpuKernel]
    public static void LocalFunctionRefParameterKernel([GpuReadOnly] uint[] r, uint[] o)
    {
        var w = new ReadOnlyUnreadOuter();
        w.Inner.Data = r;
        Set(ref w.Inner, o);
        w.Inner.Data[0] = 1u;

        static void Set(ref ReadOnlyUnreadInner inner, uint[] x) => inner.Data = x;
    }

    [GpuDeviceFunction]
    private static ReadOnlyUnreadHolder MakeHolder(uint[] o) => new() { Fixed = o, Free = o };

    [GpuDeviceFunction]
    private static ReadOnlyUnreadAttrInner MakeAttrInner(uint[] o) => new() { Data = o };

    [GpuDeviceFunction]
    private static void GetHolder(uint[] o, out ReadOnlyUnreadHolder h) =>
        h = new ReadOnlyUnreadHolder { Fixed = o, Free = o };

    [GpuDeviceFunction]
    private static void GetArray(uint[] source, out uint[] destination) => destination = source;

    [GpuDeviceFunction]
    private static void Touch(ref ReadOnlyUnreadHolder h) => h.Free = h.Fixed;

    [GpuDeviceFunction]
    private static uint ReadIn(in ReadOnlyUnreadInner inner) => inner.Data[0];
}
