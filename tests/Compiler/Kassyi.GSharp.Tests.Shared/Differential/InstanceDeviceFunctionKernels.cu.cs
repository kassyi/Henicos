#nullable enable
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial struct DiffInstanceCounter
{
    public int X;

    public DiffInstanceCounter(int x)
    {
        X = x;
    }

    [GpuDeviceFunction]
    public void Add(int v)
    {
        X += v;
    }

    [GpuDeviceFunction]
    public int AddGet(int v)
    {
        X += v;
        return X;
    }

    [GpuDeviceFunction]
    public readonly int ReadAfter(ref DiffInstanceCounter other)
    {
        other.X = 500;
        return X;
    }

    [GpuDeviceFunction]
    public readonly int ReadAfterHolder(ref DiffInlineBufHolder holder)
    {
        DiffInlineCounterBuf replacement = default;
        replacement[0] = new DiffInstanceCounter(999);
        holder.AutoBuf = replacement;
        return X;
    }

    [GpuDeviceFunction]
    public int MutAfter(ref DiffInstanceCounter other)
    {
        other.X = 600;
        return X;
    }

    [GpuDeviceFunction]
    public void Reset()
    {
        this = new DiffInstanceCounter(-1);
    }

    [GpuDeviceFunction]
    public void IncTwice()
    {
        Add(1);
        this.Add(1);
    }

    [GpuDeviceFunction]
    public int NonReadonlyPure() => X + 100;

    [GpuDeviceFunction]
    public readonly int ReadonlyCallsMutating()
    {
#pragma warning disable CS8656 // The implicit copy of this is the behavior under test
        int a = AddGet(10);
#pragma warning restore CS8656
        return a * 1000 + X;
    }
}

public static class DiffInstanceCounterExtensions
{
    [GpuDeviceFunction]
    public static int ExtReadAfter(this in DiffInstanceCounter self, ref DiffInstanceCounter other)
    {
        other.X = 500;
        return self.X;
    }

    [GpuDeviceFunction]
    public static int ExtReadAfterHolder(this in DiffInstanceCounter self, ref DiffInlineBufHolder holder)
    {
        DiffInlineCounterBuf replacement = default;
        replacement[0] = new DiffInstanceCounter(999);
        holder.AutoBuf = replacement;
        return self.X;
    }

    [GpuDeviceFunction]
    public static int ExtAddGet(this ref DiffInstanceCounter self, int v)
    {
        self.X += v;
        return self.X;
    }
}

public partial struct DiffChainInner
{
    public int X;

    [GpuDeviceFunction]
    public int AddGet(int v)
    {
        X += v;
        return X;
    }

    [GpuDeviceFunction]
    public int NonReadonlyPure() => X + 100;
}

public partial struct DiffChainMid
{
    public DiffChainInner Inner;
}

public partial struct DiffChainOuter
{
    public DiffChainMid Rw;
}

public partial struct DiffConstGetterBox
{
    public DiffChainInner Rw;
    public DiffChainMid Nested;

    public DiffConstGetterBox(int x, int nestedX)
    {
        Rw = new DiffChainInner { X = x };
        Nested = new DiffChainMid { Inner = new DiffChainInner { X = nestedX } };
    }

    public int ConstInferredCallsField => Rw.NonReadonlyPure();

    public int ConstInferredCallsNestedField => Nested.Inner.NonReadonlyPure();
}

public partial struct DiffConstGetterChainBox
{
    public DiffInlineCounterBuf RwBuf;
    public DiffInlineMidBuf Mb;
    public DiffChainInner Rw1;
    public DiffChainInner Rw2;
    public bool Flag;

    public int ConstGetterInlineBuf => RwBuf[0].NonReadonlyPure();

    public int ConstGetterInlineFieldChain => Mb[0].Rw.NonReadonlyPure();

    public int ConstGetterRefCond => (Flag ? ref Rw1 : ref Rw2).NonReadonlyPure();

    public int ConstGetterParenInlineBuf => ((RwBuf[0])).NonReadonlyPure();

    public int ConstGetterConstFalseLocalFirst
    {
        get
        {
            var local = new DiffChainInner { X = 50 };
            return (false ? ref local : ref Rw1).NonReadonlyPure();
        }
    }

    public int ConstGetterConstFalseMemberFirst
    {
        get
        {
            var local = new DiffChainInner { X = 50 };
            return (false ? ref Rw1 : ref local).NonReadonlyPure();
        }
    }

    public int ConstGetterConstFalseInlineLocalFirst
    {
        get
        {
            DiffInlineCounterBuf localBuf = default;
            return (false ? ref localBuf : ref RwBuf)[0].NonReadonlyPure();
        }
    }

    public int ConstGetterConstFalseInlineMemberFirst
    {
        get
        {
            DiffInlineCounterBuf localBuf = default;
            return (false ? ref RwBuf : ref localBuf)[0].NonReadonlyPure();
        }
    }

    public int ConstGetterRuntimeMixedMemberLocal
    {
        get
        {
            var local = new DiffChainInner { X = 70 };
            return (Flag ? ref Rw1 : ref local).NonReadonlyPure();
        }
    }
}

public partial struct DiffPrimaryBufConstGetterBox(DiffInlineCounterBuf buf)
{
    public int ConstGetterPrimaryInlineBuf => buf[0].NonReadonlyPure();
}

public unsafe partial struct DiffPtrHolder
{
    public DiffInstanceCounter* Ptr;

    public int ConstGetterPtrNonReadOnly => Ptr->NonReadonlyPure();
}

public partial struct DiffPrimaryCounter(int x)
{
    [GpuDeviceFunction]
    public readonly int GetX() => x;

    [GpuDeviceFunction]
    public int AddGet(int d)
    {
        x += d;
        return x;
    }
}

public partial struct DiffInstanceHolder
{
    public readonly DiffInstanceCounter Ro;
    public DiffInstanceCounter Rw;
    public DiffInstanceCounter Auto { get; set; }

    public DiffInstanceHolder(int x)
    {
        Ro = new DiffInstanceCounter(x);
        Rw = new DiffInstanceCounter(x);
        Auto = new DiffInstanceCounter(x);
    }

    [GpuDeviceFunction]
    public readonly int CallOnReadonlyField()
    {
#pragma warning disable CS8656 // The implicit copy of the readonly field is the behavior under test
        int a = Ro.AddGet(1);
        int b = Ro.AddGet(1);
#pragma warning restore CS8656
        return a * 1000 + b * 10 + Ro.X;
    }
}

public partial struct DiffInstanceMemberCaller
{
    public int X;

    public DiffInstanceMemberCaller(int x)
    {
        X = x;
        Add(5);
    }

    [GpuDeviceFunction]
    public void Add(int v)
    {
        X += v;
    }

    [GpuDeviceFunction]
    public int AddGet(int v)
    {
        X += v;
        return X;
    }

    [GpuDeviceFunction]
    public int NonReadonlyPure() => X + 100;

    public int MutatingProp => AddGet(1);

    public readonly int ReadonlyCallsMutatingProp
    {
        get
        {
#pragma warning disable CS8656 // The implicit copy of this is the behavior under test
            return AddGet(10);
#pragma warning restore CS8656
        }
    }

    public int ConstInferredGetter => NonReadonlyPure();
}

public readonly partial struct DiffReadOnlyPoint
{
    public readonly int X;
    public readonly int Y;

    public DiffReadOnlyPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    [GpuDeviceFunction]
    public int Sum() => X + Y;

    [GpuDeviceFunction]
    public readonly int Diff() => X - Y;
}

public partial struct DiffInstanceOverload
{
    public int X;

    public DiffInstanceOverload(int x)
    {
        X = x;
    }

    [GpuDeviceFunction]
    public static int Combine(ref DiffInstanceOverload s, int v)
    {
        s.X += v * 10;
        return s.X;
    }

    [GpuDeviceFunction]
    public int Combine(int v)
    {
        X += v;
        return X;
    }
}

public partial struct DiffParenThisHolder
{
    public readonly DiffInstanceCounter Ro;
    public DiffInstanceCounter Rw;

    public DiffParenThisHolder(int x)
    {
        Ro = new DiffInstanceCounter(x);
        ((this)).Ro.AddGet(5);
        Rw = new DiffInstanceCounter(x);
    }

    [GpuDeviceFunction]
    public int MutateParenThisRw() => ((this)).Rw.AddGet(5);
}

[InlineArray(2)]
public struct DiffInlineCounterBuf
{
    private DiffInstanceCounter _e0;
}

public partial struct DiffInlineBufHolder
{
    public DiffInlineCounterBuf AutoBuf { get; set; }
    public DiffInlineCounterBuf RwBuf;
}

[InlineArray(2)]
public struct DiffInlineMidBuf
{
    private DiffInstanceHolder _e0;
}

[SkipSnapshotConsistency("Differential kernels for struct instance device functions")]
public static partial class InstanceDeviceFunctionKernels
{
    [GpuDeviceFunction]
    private static int Bump(ref DiffInstanceCounter s)
    {
        s.X += 100;
        return 1;
    }

    [GpuDeviceFunction]
    private static unsafe DiffInstanceCounter* IdPtr(DiffInstanceCounter* p) => p;

    [GpuDeviceFunction]
    private static unsafe int BumpPtr(DiffInstanceCounter* p)
    {
        p->X += 100;
        return 1;
    }

    [GpuDeviceFunction]
    private static unsafe int MovePtr(DiffInstanceCounter** ptrSlot, DiffInstanceCounter* next)
    {
        *ptrSlot = next;
        return 1;
    }

    [GpuDeviceFunction]
    private static ref DiffInstanceCounter RefBump(ref DiffInstanceCounter s)
    {
        s.X += 100;
        return ref s;
    }

    [GpuDeviceFunction]
    private static int Log(ref int log, int tag)
    {
        log = log * 10 + tag;
        return tag;
    }

    [GpuDeviceFunction]
    private static DiffInstanceCounter Make(int x) => new DiffInstanceCounter(x);

    [GpuDeviceFunction]
    private static DiffInlineCounterBuf MakeBuf(int x)
    {
        DiffInlineCounterBuf b = default;
        b[0] = new DiffInstanceCounter(x);
        return b;
    }

    [GpuDeviceFunction]
    private static DiffInstanceHolder MakeHolder(int x) => new DiffInstanceHolder(x);

    [GpuDeviceFunction]
    private static ref DiffInstanceCounter RefTo(ref DiffInstanceCounter s, ref int log, int tag)
    {
        Log(ref log, tag);
        return ref s;
    }

    [GpuDeviceFunction]
    private static int CopyBeforeArgs(in DiffInstanceCounter p, ref DiffInstanceCounter q) =>
        p.AddGet(Bump(ref q));

    [GpuDeviceFunction]
    private static int InParamMutating(in DiffInstanceCounter p)
    {
        int a = p.AddGet(5);
        return a * 1000 + p.X;
    }

    [GpuDeviceFunction]
    private static int ReadPointThroughIn(in DiffReadOnlyPoint p) => p.Sum() * 100 + p.Diff();

    [GpuDeviceFunction]
    private static int RefReadOnlyCondMutate(bool c, in DiffInstanceCounter a, in DiffInstanceCounter b) =>
        (c ? ref a : ref b).AddGet(1);

    [GpuDeviceFunction]
    private static int MixedRefReadOnlyCondMutate(bool c, ref DiffInstanceCounter a, in DiffInstanceCounter b) =>
        (c ? ref a : ref b).AddGet(1);

    [GpuDeviceFunction]
    private static int RefReadOnlyCondRead(bool c, ref DiffInstanceCounter a, in DiffInstanceCounter b) =>
        (c ? ref a : ref b).ReadAfter(ref a);

    [GpuDeviceFunction]
    private static int ConstFalseInRefCond(in DiffInstanceCounter a, ref DiffInstanceCounter b) =>
        (false ? ref a : ref b).AddGet(1) * 10000 + a.X * 100 + b.X;

    [GpuDeviceFunction]
    private static int ConstTrueInRefCond(in DiffInstanceCounter a, ref DiffInstanceCounter b)
    {
#pragma warning disable CS8656 // Implicit defensive copy of reachable in parameter is under test
        return (true ? ref a : ref b).AddGet(1) * 10000 + a.X * 100 + b.X;
#pragma warning restore CS8656
    }

    [GpuDeviceFunction]
    private static int InlineInHelper(in DiffInlineCounterBuf b)
    {
#pragma warning disable CS8656 // Implicit defensive copy of readonly inline array element is under test
        return b[0].AddGet(1);
#pragma warning restore CS8656
    }

    [GpuDeviceFunction]
    private static int ConstFalseInlineRefCond(in DiffInlineCounterBuf bIn, ref DiffInlineCounterBuf bRw)
    {
#pragma warning disable CS8656 // Implicit defensive copy of readonly inline array ref conditional is under test
        int ret = (false ? ref bIn : ref bRw)[0].AddGet(1);
#pragma warning restore CS8656
        return ret * 100 + bRw[0].X;
    }

    [GpuDeviceFunction]
    private static int ConstTrueRwFirstInlineRefCond(ref DiffInlineCounterBuf bRw, in DiffInlineCounterBuf bIn)
    {
#pragma warning disable CS8656 // Implicit defensive copy of readonly inline array ref conditional is under test
        int ret = (true ? ref bRw : ref bIn)[0].AddGet(1);
#pragma warning restore CS8656
        return ret * 100 + bRw[0].X;
    }

    [GpuDeviceFunction]
    private static int ConstFalseHolderInlineRefCond(in DiffInlineBufHolder hIn, ref DiffInlineBufHolder hRw)
    {
#pragma warning disable CS8656 // Implicit defensive copy of readonly inline array field on ref conditional is under test
        int ret = (false ? ref hIn : ref hRw).RwBuf[0].AddGet(1);
#pragma warning restore CS8656
        return ret * 100 + hRw.RwBuf[0].X;
    }

    // O1: ref receiver, argument writes it
    [GpuKernel]
    public static void RefReceiverArgWritesKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(data[t]);
        s.Add(Bump(ref s));
        r[t] = s.X;
    }

    // O2: value receiver, mutating
    [GpuKernel]
    public static void ValueReceiverMutatingKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        r[t] = Make(6 + data[t]).AddGet(3);
    }

    // O3: readonly method, alias write
    [GpuKernel]
    public static void ReadonlyMemberAliasWriteKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(1 + data[t]);
        int ret = s.ReadAfter(ref s);
        r[t * 2] = ret;
        r[t * 2 + 1] = s.X;
    }

    // O4: mutating method, alias write
    [GpuKernel]
    public static void MutatingMemberAliasWriteKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(2 + data[t]);
        int ret = s.MutAfter(ref s);
        r[t * 2] = ret;
        r[t * 2 + 1] = s.X;
    }

    // O5: in receiver copied before the arguments
    [GpuKernel]
    public static void InReceiverCopyBeforeArgsKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(3 + data[t]);
        int ret = CopyBeforeArgs(in s, ref s);
        r[t * 2] = ret;
        r[t * 2 + 1] = s.X;
    }

    // O6: in parameter, mutating call
    [GpuKernel]
    public static void InParamMutatingCallKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(4 + data[t]);
        int ret = InParamMutating(in s);
        r[t * 2] = ret;
        r[t * 2 + 1] = s.X;
    }

    // O7: this = new S(-1)
    [GpuKernel]
    public static void ThisReassignKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(5 + data[t]);
        s.Reset();
        r[t] = s.X;
    }

    // O8: implicit and explicit this calls
    [GpuKernel]
    public static void ImplicitAndExplicitThisKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(6 + data[t]);
        s.IncTwice();
        r[t] = s.X;
    }

    // O9: readonly member calls a mutating one on this
    [GpuKernel]
    public static void ReadonlyCallsMutatingKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceCounter(7 + data[t]);
        int ret = s.ReadonlyCallsMutating();
        r[t * 2] = ret;
        r[t * 2 + 1] = s.X;
    }

    // O10: readonly field receiver
    [GpuKernel]
    public static void ReadonlyFieldReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var h = new DiffInstanceHolder(8 + data[t]);
        int ret = h.CallOnReadonlyField();
        r[t * 2] = ret;
        r[t * 2 + 1] = h.Ro.X;
    }

    // O11: field of a local
    [GpuKernel]
    public static void FieldOfLocalReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var h = new DiffInstanceHolder(8 + data[t]);
        h.Rw.Add(5);
        r[t] = h.Rw.X;
    }

    // Auto-property receiver (value, not variable, so mutating call must not alias backing field)
    [GpuKernel]
    public static void AutoPropertyReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var h = new DiffInstanceHolder(8 + data[t]);
        int ret = h.Auto.AddGet(5);
        r[t * 2] = ret;
        r[t * 2 + 1] = h.Auto.X;
    }

    // O12: receiver before the arguments (ref-returning static device function as receiver)
    [GpuKernel]
    public static void ReceiverBeforeArgsKernel(int[] data, DiffInstanceCounter[] items, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int k = 0;
        int log = 0;
        int baseIdx = t * 3;
        RefTo(ref items[baseIdx + k++], ref log, data[t]).Add(Log(ref log, 2) + k);
        r[t * 2] = log;
        r[t * 2 + 1] = k;
    }

    // O13: element receiver with a side effect in the index
    [GpuKernel]
    public static void ElementReceiverIndexSideEffectKernel(int[] data, DiffInstanceCounter[] items, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int k = 0;
        int baseIdx = t * 3;
        items[baseIdx + k++].Add(k * 10 * data[t]);
        r[t * 2] = items[baseIdx].X;
        r[t * 2 + 1] = items[baseIdx + 1].X;
    }

    // T2: receivers inside struct constructors and property getters
    [GpuKernel]
    public static void StructMembersCallInstanceDeviceFunctionKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var m = new DiffInstanceMemberCaller(data[t]);
        int ctorX = m.X;
        int mutProp = m.MutatingProp;
        int afterMutProp = m.X;
        int roProp = m.ReadonlyCallsMutatingProp;
        int afterRoProp = m.X;
        int constInferred = m.ConstInferredGetter;
        int o = t * 6;
        r[o + 0] = ctorX;
        r[o + 1] = mutProp;
        r[o + 2] = afterMutProp;
        r[o + 3] = roProp;
        r[o + 4] = afterRoProp;
        r[o + 5] = constInferred;
    }

    // T3: readonly struct methods (including called on an in parameter)
    [GpuKernel]
    public static void ReadOnlyStructDeviceFunctionKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var p = new DiffReadOnlyPoint(10 + data[t], 3);
        r[t] = ReadPointThroughIn(in p);
    }

    // T7: static M(ref S, int) and instance M(int) on the same struct
    [GpuKernel]
    public static void StaticAndInstanceOverloadKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var s = new DiffInstanceOverload(data[t]);
        int staticRet = DiffInstanceOverload.Combine(ref s, 3);
        int instanceRet = s.Combine(4);
        r[t * 2] = staticRet;
        r[t * 2 + 1] = instanceRet;
    }

    // Readonly instance & reduced extension calls on assignment / conditional rvalue receivers
    [GpuKernel]
    public static void RvalueLvalueAliasReadonlyKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var s1 = new DiffInstanceCounter(d);
        var s2 = new DiffInstanceCounter(d + 1);
        int assignRet = (s1 = s2).ReadAfter(ref s1);
        int assignAfter = s1.X;

        var s3 = new DiffInstanceCounter(d + 9);
        var s4 = new DiffInstanceCounter(d + 19);
        bool cond = (d & 1) == 1;
        int condRet = (cond ? s3 : s4).ReadAfter(ref s3);
        int condAfter = s3.X;

        var s5 = new DiffInstanceCounter(d + 2);
        var s6 = new DiffInstanceCounter(d + 3);
        int extRet = (s5 = s6).ExtReadAfter(ref s5);
        int extAfter = s5.X;

        int o = t * 6;
        r[o + 0] = assignRet;
        r[o + 1] = assignAfter;
        r[o + 2] = condRet;
        r[o + 3] = condAfter;
        r[o + 4] = extRet;
        r[o + 5] = extAfter;
    }

    // Mutating instance calls on member chains rooted at conditional / assignment rvalues
    [GpuKernel]
    public static void RvalueLvalueMemberChainMutatingKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var ha = new DiffChainOuter { Rw = new DiffChainMid { Inner = new DiffChainInner { X = d } } };
        var hb = new DiffChainOuter { Rw = new DiffChainMid { Inner = new DiffChainInner { X = d + 10 } } };
        bool cond = (d & 1) == 1;
        int condRet = (cond ? ha : hb).Rw.Inner.AddGet(7);

        var ha2 = new DiffChainOuter { Rw = new DiffChainMid { Inner = new DiffChainInner { X = d + 20 } } };
        var hb2 = new DiffChainOuter { Rw = new DiffChainMid { Inner = new DiffChainInner { X = d + 30 } } };
        int assignRet = (ha2 = hb2).Rw.Inner.AddGet(7);

        int o = t * 4;
        r[o + 0] = condRet;
        r[o + 1] = ha.Rw.Inner.X;
        r[o + 2] = assignRet;
        r[o + 3] = ha2.Rw.Inner.X;
    }

    // Primary-constructor struct instance [GpuDeviceFunction] methods
    [GpuKernel]
    public static void PrimaryConstructorDeviceFunctionKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var c = new DiffPrimaryCounter(data[t] + 10);
        int before = c.GetX();
        int after = c.AddGet(3);
        int readAfter = c.GetX();
        int o = t * 3;
        r[o + 0] = before;
        r[o + 1] = after;
        r[o + 2] = readAfter;
    }

    // Const-inferred non-readonly getter calling non-readonly pure device function on a field of this
    [GpuKernel]
    public static void ConstInferredGetterFieldKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        var box = new DiffConstGetterBox(data[t] + 5, data[t] + 15);
        r[t * 2] = box.ConstInferredCallsField;
        r[t * 2 + 1] = box.ConstInferredCallsNestedField;
    }

    // Parenthesized member-access and reduced extension calls preserve left-to-right operand evaluation order
    [GpuKernel]
    public static void ParenthesizedMemberCallOrderKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var s1 = new DiffInstanceCounter(9 + d);
        int first = s1.X + ((s1.AddGet))(1);
        var s2 = new DiffInstanceCounter(9 + d);
        int second = ((s2.AddGet))(1) + s2.X;
        var s3 = new DiffInstanceCounter(9 + d);
        int ext = s3.X + ((s3.ExtAddGet))(1);
        int o = t * 3;
        r[o + 0] = first;
        r[o + 1] = second;
        r[o + 2] = ext;
    }

    // ref-readonly conditional receivers (in/in and mixed ref/in) copy on mutating calls and alias on readonly calls
    [GpuKernel]
    public static void RefReadOnlyConditionalReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        bool cond = (d & 1) == 1;
        var a = new DiffInstanceCounter(9 + d);
        var b = new DiffInstanceCounter(19 + d);
        int roRet = RefReadOnlyCondMutate(cond, in a, in b);
        int mixedRet = MixedRefReadOnlyCondMutate(cond, ref a, in b);
        int afterMutateA = a.X;
        int readRet = RefReadOnlyCondRead(cond, ref a, in b);
        int o = t * 6;
        r[o + 0] = roRet;
        r[o + 1] = mixedRet;
        r[o + 2] = afterMutateA;
        r[o + 3] = b.X;
        r[o + 4] = readRet;
        r[o + 5] = a.X;
    }

    // Constant-condition ref conditional follows Roslyn lowering (only the selected branch determines mutability)
    [GpuKernel]
    public static void ConstConditionRefConditionalKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var s1 = new DiffInstanceCounter(9 + d);
        var s2 = new DiffInstanceCounter(19 + d);
        ref readonly var r1 = ref s1;
        int falseMutRet = (false ? ref r1 : ref s2).AddGet(1);
        int falseCombined = falseMutRet * 10000 + s1.X * 100 + s2.X;

        var s3 = new DiffInstanceCounter(19 + d);
        var rSrc = new DiffInstanceCounter(9 + d);
        ref readonly var r2 = ref rSrc;
        int trueMutSecondRet = (true ? ref s3 : ref r2).AddGet(1);
        int trueSecondCombined = trueMutSecondRet * 10000 + s3.X * 100 + rSrc.X;

        var pIn1 = new DiffInstanceCounter(9 + d);
        var pRef1 = new DiffInstanceCounter(19 + d);
        int inFalseCombined = ConstFalseInRefCond(in pIn1, ref pRef1);

        var pIn2 = new DiffInstanceCounter(9 + d);
        var pRef2 = new DiffInstanceCounter(19 + d);
        int inTrueCombined = ConstTrueInRefCond(in pIn2, ref pRef2);

        int o = t * 4;
        r[o + 0] = falseCombined;
        r[o + 1] = trueSecondCombined;
        r[o + 2] = inFalseCombined;
        r[o + 3] = inTrueCombined;
    }

    // Parenthesized ((this)) field receivers in constructor and method emit direct address without __gsharp_temp_address
    [GpuKernel]
    public static void ParenthesizedThisFieldKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var h = new DiffParenThisHolder(9 + d);
        int ctorRo = h.Ro.X;
        int rwRet = h.MutateParenThisRw();
        int rwAfter = h.Rw.X;
        int o = t * 3;
        r[o + 0] = ctorRo;
        r[o + 1] = rwRet;
        r[o + 2] = rwAfter;
    }

    // InlineArray element of an in parameter copies on mutating instance call; local InlineArray element mutates in place
    [GpuKernel]
    public static void InlineArrayInParamKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        DiffInlineCounterBuf b = default;
        b[0] = new DiffInstanceCounter(9 + d);
        int inRet = InlineInHelper(in b);
        int inAfter = b[0].X;

        DiffInlineCounterBuf bMut = default;
        bMut[0] = new DiffInstanceCounter(9 + d);
        int mutRet = bMut[0].AddGet(1);
        int mutAfter = bMut[0].X;

        int o = t * 2;
        r[o + 0] = inRet * 100 + inAfter;
        r[o + 1] = mutRet * 100 + mutAfter;
    }

    // Rvalue InlineArray elements and rvalue field/auto-property chains materialize prvalues for mutating instance calls
    [GpuKernel]
    public static void RvalueSubobjectTemporaryReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        int c0 = MakeBuf(10 + d)[0].AddGet(1);

        bool cond = (d & 1) == 1;
        int c1 = (cond ? MakeBuf(10 + d) : MakeBuf(20 + d))[0].AddGet(1);

        DiffInlineCounterBuf b1 = default;
        DiffInlineCounterBuf b2 = default;
        b2[0] = new DiffInstanceCounter(20 + d);
        int c2 = (b1 = b2)[0].AddGet(1);
        int b1After = b1[0].X;

        var bh = new DiffInlineBufHolder { AutoBuf = MakeBuf(30 + d) };
        int c3 = bh.AutoBuf[0].AddGet(1);
        int bhAfter = bh.AutoBuf[0].X;

        int c4 = MakeHolder(40 + d).Rw.AddGet(1);
        int c5 = MakeHolder(50 + d).Auto.AddGet(1);
        int c6 = new DiffInstanceHolder(60 + d).Rw.AddGet(1);
        int c7 = ((d & 1) switch { 1 => MakeHolder(70 + d), _ => MakeHolder(80 + d) }).Rw.AddGet(1);

        var s1 = new DiffInstanceCounter(5 + d);
        int c8 = MakeBuf(10 + s1.X)[0].AddGet(Bump(ref s1));

        var s2 = new DiffInstanceCounter(5 + d);
        int c9 = MakeHolder(20 + s2.X).Rw.AddGet(Bump(ref s2));

        int o = t * 12;
        r[o + 0] = c0;
        r[o + 1] = c1;
        r[o + 2] = c2;
        r[o + 3] = b1After;
        r[o + 4] = c3;
        r[o + 5] = bhAfter;
        r[o + 6] = c4;
        r[o + 7] = c5;
        r[o + 8] = c6;
        r[o + 9] = c7;
        r[o + 10] = c8 * 1000 + s1.X;
        r[o + 11] = c9 * 1000 + s2.X;
    }

    // Readonly instance and extension calls on temporary [InlineArray] elements copy instead of aliasing storage
    [GpuKernel]
    public static void ReadOnlyInlineArrayTemporaryReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        DiffInlineCounterBuf b1 = default;
        DiffInlineCounterBuf b2 = default;
        b2[0] = new DiffInstanceCounter(20 + d);
        int c0 = (b1 = b2)[0].ReadAfter(ref b1[0]);

        DiffInlineCounterBuf b3 = default;
        DiffInlineCounterBuf b4 = default;
        b4[0] = new DiffInstanceCounter(25 + d);
        int c1 = (b3 = b4)[0].ExtReadAfter(ref b3[0]);

        var bh1 = new DiffInlineBufHolder { AutoBuf = MakeBuf(30 + d) };
        int c2 = bh1.AutoBuf[0].ReadAfterHolder(ref bh1);

        var bh2 = new DiffInlineBufHolder { AutoBuf = MakeBuf(35 + d) };
        int c3 = bh2.AutoBuf[0].ExtReadAfterHolder(ref bh2);

        DiffInlineCounterBuf cb1 = default;
        DiffInlineCounterBuf cb2 = default;
        cb1[0] = new DiffInstanceCounter(55 + d);
        cb2[0] = new DiffInstanceCounter(65 + d);
        bool cond = (d & 1) == 1;
        int c4 = (cond ? cb1 : cb2)[0].ReadAfter(ref cb1[0]);

        DiffInlineMidBuf mb1 = default;
        DiffInlineMidBuf mb2 = default;
        mb2[0] = new DiffInstanceHolder(75 + d);
        int c5 = (mb1 = mb2)[0].Rw.ReadAfter(ref mb1[0].Rw);

        DiffInlineCounterBuf b5 = default;
        DiffInlineCounterBuf b6 = default;
        b6[0] = new DiffInstanceCounter(85 + d);
        int c6 = (b5 = b6)[0].ReadAfter(ref RefBump(ref b6[0]));

        int o = t * 10;
        r[o + 0] = c0;
        r[o + 1] = b1[0].X;
        r[o + 2] = c1;
        r[o + 3] = c2;
        r[o + 4] = bh1.AutoBuf[0].X;
        r[o + 5] = c3;
        r[o + 6] = bh2.AutoBuf[0].X;
        r[o + 7] = c4;
        r[o + 8] = c5;
        r[o + 9] = c6 * 1000 + b5[0].X;
    }

    // Constant-condition mixed ref/in conditional [InlineArray] indexing binds as ref readonly in C#
    [GpuKernel]
    public static void ConstConditionInlineArrayReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        DiffInlineCounterBuf bIn = default;
        bIn[0] = new DiffInstanceCounter(9 + d);
        DiffInlineCounterBuf bRw = default;
        bRw[0] = new DiffInstanceCounter(40 + d);
        int cFalse = ConstFalseInlineRefCond(in bIn, ref bRw);
        int cTrueRwFirst = ConstTrueRwFirstInlineRefCond(ref bRw, in bIn);

        DiffInlineCounterBuf bRw2 = default;
        bRw2[0] = new DiffInstanceCounter(50 + d);
        int cWritableRet = (false ? ref bRw : ref bRw2)[0].AddGet(1);
        int cWritable = cWritableRet * 100 + bRw2[0].X;

        DiffInlineBufHolder hIn = default;
        hIn.RwBuf[0] = new DiffInstanceCounter(9 + d);
        DiffInlineBufHolder hRw = default;
        hRw.RwBuf[0] = new DiffInstanceCounter(60 + d);
        int cHolder = ConstFalseHolderInlineRefCond(in hIn, ref hRw);

        int o = t * 4;
        r[o + 0] = cFalse;
        r[o + 1] = cTrueRwFirst;
        r[o + 2] = cWritable;
        r[o + 3] = cHolder;
    }

    // Const-inferred non-readonly getters calling non-readonly pure instance methods on InlineArray elements,
    // InlineArray field chains, primary-constructor InlineArray parameters, and ref conditionals
    [GpuKernel]
    public static void ConstInferredGetterSubobjectKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var box = new DiffConstGetterChainBox();
        box.RwBuf[0] = new DiffInstanceCounter(10 + d);
        box.Mb[0] = new DiffInstanceHolder(10 + d);
        box.Rw1 = new DiffChainInner { X = 10 + d };
        box.Rw2 = new DiffChainInner { X = 20 + d };
        box.Flag = (d & 1) == 1;

        var primaryBox = new DiffPrimaryBufConstGetterBox(box.RwBuf);

        int o = t * 10;
        r[o + 0] = box.ConstGetterInlineBuf;
        r[o + 1] = primaryBox.ConstGetterPrimaryInlineBuf;
        r[o + 2] = box.ConstGetterInlineFieldChain;
        r[o + 3] = box.ConstGetterRefCond;
        r[o + 4] = box.ConstGetterParenInlineBuf;
        r[o + 5] = box.ConstGetterConstFalseLocalFirst;
        r[o + 6] = box.ConstGetterConstFalseMemberFirst;
        r[o + 7] = box.ConstGetterConstFalseInlineLocalFirst;
        r[o + 8] = box.ConstGetterConstFalseInlineMemberFirst;
        r[o + 9] = box.ConstGetterRuntimeMixedMemberLocal;
    }

    // Pointer-member calls (->) on pointer locals, parenthesized pointers, conditional pointers, prvalue pointer returns,
    // pointer fields inside const-inferred getters, and evaluation-order interference
    [GpuKernel]
    public static unsafe void PointerArrowReceiverKernel(int[] data, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= data.Length)
        {
            return;
        }

        int d = data[t];
        var s1 = new DiffInstanceCounter(d);
        DiffInstanceCounter* p1 = &s1;
        int a0 = p1->AddGet(5);
        int a1 = p1->AddGet(BumpPtr(p1));

        var s2 = new DiffInstanceCounter(10 + d);
        DiffInstanceCounter* p2 = &s2;
        int a2 = ((p2))->AddGet(5);

        var s3a = new DiffInstanceCounter(20 + d);
        var s3b = new DiffInstanceCounter(30 + d);
        DiffInstanceCounter* p3a = &s3a;
        DiffInstanceCounter* p3b = &s3b;
        bool flag = (d & 1) == 1;
        int a3 = (flag ? p3a : p3b)->AddGet(5);

        var s4 = new DiffInstanceCounter(40 + d);
        int a4 = IdPtr(&s4)->AddGet(5);

        var s5 = new DiffInstanceCounter(50 + d);
        int a5 = IdPtr(&s5)->AddGet(Bump(ref s5));

        var s6 = new DiffInstanceCounter(60 + d);
        var ptrHolder = new DiffPtrHolder { Ptr = &s6 };
        int a6 = ptrHolder.ConstGetterPtrNonReadOnly;

        var sMoveA = new DiffInstanceCounter(70 + d);
        var sMoveB = new DiffInstanceCounter(80 + d);
        DiffInstanceCounter* pMove = &sMoveA;
        int a7 = pMove->AddGet(MovePtr(&pMove, &sMoveB));

        var sAlias = new DiffInstanceCounter(90 + d);
        DiffInstanceCounter* pAlias = &sAlias;
        DiffInstanceCounter* pAlias2 = &sAlias;
        int a8 = pAlias->AddGet(5) * 1000 + (*pAlias2).X;

        var sRead1 = new DiffInstanceCounter(5 + d);
        DiffInstanceCounter* pRead1 = &sRead1;
        int a9 = pRead1->ReadAfter(ref *pRead1);

        var sRead2 = new DiffInstanceCounter(7 + d);
        int a10 = IdPtr(&sRead2)->ReadAfter(ref sRead2);

        int o = t * 11;
        r[o + 0] = a0;
        r[o + 1] = a1;
        r[o + 2] = a2;
        r[o + 3] = a3 * 100 + s3a.X;
        r[o + 4] = a4 * 100 + s4.X;
        r[o + 5] = a5 * 1000 + s5.X;
        r[o + 6] = a6;
        r[o + 7] = a7 * 10000 + sMoveA.X * 100 + sMoveB.X;
        r[o + 8] = a8;
        r[o + 9] = a9 * 1000 + sRead1.X;
        r[o + 10] = a10 * 1000 + sRead2.X;
    }
}
