using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace SuspensionAnalysisCases;

public static partial class Functions
{
    [GpuDeviceFunction]
    public static int BarrierLeaf(int x)
    {
        Gpu.SyncThreads();
        return x + 1;
    }

    [GpuDeviceFunction]
    public static int PlainLeaf(int x) => x + 1;

    [GpuDeviceFunction]
    public static int ViaCtor(int x)
    {
        var h = new BarrierCtor(x);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPlainCtor(int x)
    {
        var h = new PlainCtor(x);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPropertyGetter(int x)
    {
        var h = new GetterBarrierProp { X = x };
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPlainPropertyGetter(int x)
    {
        var h = new PlainProp { X = x };
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPropertySetter(int x)
    {
        var h = new SetterBarrierProp();
        h.Value = x;
        return h.X;
    }

    [GpuDeviceFunction]
    public static int ViaGetterOnlyAssignmentOnly(int x)
    {
        var h = new GetterBarrierProp();
        h.Value = x;
        return h.X;
    }

    [GpuDeviceFunction]
    public static int ViaCompoundAssignment(int x)
    {
        var h = new GetterBarrierProp();
        h.Value += x;
        return h.X;
    }

    [GpuDeviceFunction]
    public static int ViaChainedCtor(int x)
    {
        var h = new ChainedBarrierCtor(x);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPlainChainedCtor(int x)
    {
        var h = new ChainedPlainCtor(x);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaFieldInitializer()
    {
        var h = new FieldInitBarrier();
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPlainFieldInitializer()
    {
        var h = new FieldInitPlain();
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaIndexer(int x)
    {
        var h = new IndexerBarrier { X = x };
        return h[0];
    }

    [GpuDeviceFunction]
    public static int ViaPlainIndexer(int x)
    {
        var h = new IndexerPlain { X = x };
        return h[0];
    }

    [GpuDeviceFunction]
    public static int ViaImplicitConversion(int x)
    {
        var h = new ConversionBarrier { X = x };
        int v = h;
        return v;
    }

    [GpuDeviceFunction]
    public static int ViaPlainImplicitConversion(int x)
    {
        var h = new ConversionPlain { X = x };
        int v = h;
        return v;
    }

    [GpuDeviceFunction]
    public static int ViaCollectionBuilder()
    {
        BarrierBag bag = [1, 2];
        return bag.First;
    }

    [GpuDeviceFunction]
    public static int ViaPlainCollectionBuilder()
    {
        PlainBag bag = [1, 2];
        return bag.First;
    }

    [GpuDeviceFunction]
    public static void CtorOnlyBody(int x)
    {
        var h = new BarrierCtor(x);
        _ = h.Value;
    }

    [GpuDeviceFunction]
    public static void CtorAndDeviceFunctionBody(int x)
    {
        var h = new BarrierCtor(x);
        _ = BarrierLeaf(h.Value);
    }

    [GpuDeviceFunction]
    public static void PartialStructGenericDeviceFunctionBody()
    {
        PartialStructHelperAttrOnImpl.PartialHelper<int>();
    }

    [GpuDeviceFunction]
    public static void PartialGenericStructDeviceFunctionBody()
    {
        PartialGenStructAttrOnImpl<int>.PartialHelper();
    }

    [GpuDeviceFunction]
    public static int ViaImplicitDefaultCtorOnFieldInitStruct()
    {
        var h = new FieldInitWithParamCtor();
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaParamCtorOnFieldInitStruct()
    {
        var h = new FieldInitWithParamCtor(1);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaImplicitDefaultCtorOnFieldInitRecordStruct()
    {
        var h = new FieldInitRecordStruct();
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaPrimaryCtorOnFieldInitRecordStruct()
    {
        var h = new FieldInitRecordStruct(1);
        return h.Value;
    }

    [GpuDeviceFunction]
    public static int ViaNestedTupleDeconstructionGetterOnly(int x)
    {
        var h = new GetterBarrierProp();
        int a;
        int b;
        ((h.Value, a), b) = ((x, 2), 3);
        return h.X + a + b;
    }

    [GpuDeviceFunction]
    public static int ViaNestedTupleDeconstructionSetterOnly(int x)
    {
        var h = new SetterBarrierProp();
        int a;
        int b;
        ((h.Value, a), b) = ((x, 2), 3);
        return h.X + a + b;
    }

    [GpuDeviceFunction]
    public static int ViaGenericConstrainedBarrier() => new GenericConstrainedCtor<BarrierAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericConstrainedPlain() => new GenericConstrainedCtor<PlainAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericNewTBarrier() => new GenericNewTCtor<CtorBarrierAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericNewTPlain() => new GenericNewTCtor<PlainCtorAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericUsingTBarrier() => new GenericUsingTCtor<DisposableBarrierAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericUsingTPlain() => new GenericUsingTCtor<PlainDisposableAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedOuterInnerBarrier() => new Outer<BarrierAction>.Inner(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedOuterInnerPlain() => new Outer<PlainAction>.Inner(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedOuterNestedFirstBarrier() => new Outer<BarrierAction>.Nested<PlainAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedOuterNestedSecondBarrier() => new Outer<PlainAction>.Nested<BarrierAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedOuterNestedPlain() => new Outer<PlainAction>.Nested<PlainAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedMultiLevelBarrier() => new Outer<BarrierAction>.Mid.Leaf(1).Value;

    [GpuDeviceFunction]
    public static int ViaNestedMultiLevelPlain() => new Outer<PlainAction>.Mid.Leaf(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericBoxPropBarrier()
    {
        var b = new GenericBox<BarrierAction>();
        return b.Prop;
    }

    [GpuDeviceFunction]
    public static int ViaGenericBoxPropPlain()
    {
        var b = new GenericBox<PlainAction>();
        return b.Prop;
    }

    [GpuDeviceFunction]
    public static void SetInterfaceProp<T>(ref T p, int v) where T : struct, IHasProp => p.Prop = v;

    [GpuDeviceFunction]
    public static int ViaExplicitInterfacePropSetterBarrier()
    {
        var p = new ExplicitPropBarrier();
        SetInterfaceProp(ref p, 1);
        return p.X;
    }

    [GpuDeviceFunction]
    public static int ViaExplicitInterfacePropSetterPlain()
    {
        var p = new PlainPropStruct();
        SetInterfaceProp(ref p, 1);
        return p.X;
    }

    [GpuDeviceFunction]
    public static void GenericDeviceCall<T>() where T : struct, IAction => default(T).Run();

    [GpuDeviceFunction]
    public static int ViaGenericNestedOuterInner<T>() where T : struct, IAction => new Outer<T>.Inner(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericNestedBoth<T>() where T : struct, IAction => new Outer<T>.Nested<T>(1).Value;

    [GpuDeviceFunction]
    public static int ViaGenericNestedMultiLevel<T>() where T : struct, IAction => new Outer<T>.Mid.Leaf(1).Value;

    [GpuDeviceFunction]
    public static void DeepPlain<T>(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        DeepPlain<Wrap<T>>(depth - 1);
    }

    [GpuDeviceFunction]
    public static void DeepBarrier<T>(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        DeepBarrier<Wrap<T>>(depth - 1);
    }

    [GpuDeviceFunction]
    public static void RecursiveGeneric<T>(int depth) where T : struct, IAction
    {
        if (depth <= 0)
        {
            return;
        }

        default(T).Run();
        RecursiveGeneric<T>(depth - 1);
    }

    [GpuDeviceFunction]
    public static void CallConstrainedGenMethod<T, U>() where T : struct, IActionGen where U : struct, IAction =>
        default(T).Run<U>();

    [GpuDeviceFunction]
    public static int ViaConstrainedGenMethodCtorBarrier() =>
        new GenericConstrainedGenMethodCtor<ImplActionGen, BarrierAction>(1).Value;

    [GpuDeviceFunction]
    public static int ViaConstrainedGenMethodCtorPlain() =>
        new GenericConstrainedGenMethodCtor<ImplActionGen, PlainAction>(1).Value;

    [GpuDeviceFunction]
    public static void CallConstrainedOfGenMethod<T, TArg, U>()
        where T : struct, IActionOfGen<TArg>
        where TArg : struct, IAction
        where U : struct, IAction =>
        default(T).Run<U>();

    [GpuDeviceFunction]
    public static partial void PartialGeneric<T>() where T : struct, IAction;

    public static partial void PartialGeneric<T>() where T : struct, IAction => default(T).Run();

    public static partial void PartialGenericAttrOnImpl<T>() where T : struct, IAction;

    [GpuDeviceFunction]
    public static partial void PartialGenericAttrOnImpl<T>() where T : struct, IAction => default(T).Run();

    [GpuDeviceFunction]
    public static partial void RecPartialGeneric<T>(int depth) where T : struct, IAction;

    public static partial void RecPartialGeneric<T>(int depth) where T : struct, IAction
    {
        if (depth <= 0)
        {
            return;
        }

        default(T).Run();
        RecPartialGeneric<T>(depth - 1);
    }

    [GpuDeviceFunction]
    public static partial int RecPartialExt(this int depth);

    public static partial int RecPartialExt(this int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return 0;
        }

        return (depth - 1).RecPartialExt();
    }

    [GpuDeviceFunction]
    public static int RecNonPartialExt(this int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return 0;
        }

        return (depth - 1).RecNonPartialExt();
    }

    [GpuDeviceFunction]
    public static partial void PartialDeepPlainAttrDef<T>(int depth);

    public static partial void PartialDeepPlainAttrDef<T>(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        PartialDeepPlainAttrDef<Wrap<T>>(depth - 1);
    }

    [GpuDeviceFunction]
    public static partial void PartialDeepBarrierAttrDef<T>(int depth);

    public static partial void PartialDeepBarrierAttrDef<T>(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        PartialDeepBarrierAttrDef<Wrap<T>>(depth - 1);
    }

    public static partial void PartialDeepPlainAttrImpl<T>(int depth);

    [GpuDeviceFunction]
    public static partial void PartialDeepPlainAttrImpl<T>(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        PartialDeepPlainAttrImpl<Wrap<T>>(depth - 1);
    }

    public static partial void PartialDeepBarrierAttrImpl<T>(int depth);

    [GpuDeviceFunction]
    public static partial void PartialDeepBarrierAttrImpl<T>(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        PartialDeepBarrierAttrImpl<Wrap<T>>(depth - 1);
    }

    [GpuDeviceFunction]
    public static partial int PartialDeepExtPlainAttrDef<T>(this int depth, T value);

    public static partial int PartialDeepExtPlainAttrDef<T>(this int depth, T value) =>
        depth <= 0 ? 0 : (depth - 1).PartialDeepExtPlainAttrDef(new Wrap<T> { Value = value });

    [GpuDeviceFunction]
    public static partial int PartialDeepExtBarrierAttrDef<T>(this int depth, T value);

    public static partial int PartialDeepExtBarrierAttrDef<T>(this int depth, T value)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return 0;
        }

        return (depth - 1).PartialDeepExtBarrierAttrDef(new Wrap<T> { Value = value });
    }

    public static partial int PartialDeepExtPlainAttrImpl<T>(this int depth, T value);

    [GpuDeviceFunction]
    public static partial int PartialDeepExtPlainAttrImpl<T>(this int depth, T value) =>
        depth <= 0 ? 0 : (depth - 1).PartialDeepExtPlainAttrImpl(new Wrap<T> { Value = value });

    public static partial int PartialDeepExtBarrierAttrImpl<T>(this int depth, T value);

    [GpuDeviceFunction]
    public static partial int PartialDeepExtBarrierAttrImpl<T>(this int depth, T value)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return 0;
        }

        return (depth - 1).PartialDeepExtBarrierAttrImpl(new Wrap<T> { Value = value });
    }

    [GpuDeviceFunction]
    public static partial void PartialIndirectPlainAttrDef<T>(int depth);

    public static partial void PartialIndirectPlainAttrDef<T>(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        HelperForPartialPlainDef<T>(depth);
    }

    [GpuDeviceFunction]
    public static void HelperForPartialPlainDef<U>(int depth) => PartialIndirectPlainAttrDef<Wrap<U>>(depth - 1);

    [GpuDeviceFunction]
    public static partial void PartialIndirectBarrierAttrDef<T>(int depth);

    public static partial void PartialIndirectBarrierAttrDef<T>(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        HelperForPartialBarrierDef<T>(depth);
    }

    [GpuDeviceFunction]
    public static void HelperForPartialBarrierDef<U>(int depth) => PartialIndirectBarrierAttrDef<Wrap<U>>(depth - 1);

    public static partial void PartialIndirectPlainAttrImpl<T>(int depth);

    [GpuDeviceFunction]
    public static partial void PartialIndirectPlainAttrImpl<T>(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        HelperForPartialPlainImpl<T>(depth);
    }

    [GpuDeviceFunction]
    public static void HelperForPartialPlainImpl<U>(int depth) => PartialIndirectPlainAttrImpl<Wrap<U>>(depth - 1);

    public static partial void PartialIndirectBarrierAttrImpl<T>(int depth);

    [GpuDeviceFunction]
    public static partial void PartialIndirectBarrierAttrImpl<T>(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        HelperForPartialBarrierImpl<T>(depth);
    }

    [GpuDeviceFunction]
    public static void HelperForPartialBarrierImpl<U>(int depth) => PartialIndirectBarrierAttrImpl<Wrap<U>>(depth - 1);

    public static partial void PartialConstrainedAttrImpl<T>(int depth) where T : struct, IDepthAction;

    [GpuDeviceFunction]
    public static partial void PartialConstrainedAttrImpl<T>(int depth) where T : struct, IDepthAction
    {
        if (depth <= 0)
        {
            return;
        }

        RelayConstrainedAttrImpl<T>(depth);
    }

    public static partial void RelayConstrainedAttrImpl<V>(int depth) where V : struct, IDepthAction;

    [GpuDeviceFunction]
    public static partial void RelayConstrainedAttrImpl<V>(int depth) where V : struct, IDepthAction =>
        default(V).Run(depth);

    public static partial void PartialSwap<T, U>(int depth)
        where T : struct, IAction
        where U : struct, IAction;

    [GpuDeviceFunction]
    public static partial void PartialSwap<T, U>(int depth)
        where T : struct, IAction
        where U : struct, IAction
    {
        if (depth <= 0)
        {
            default(T).Run();
            return;
        }

        PartialSwap<U, T>(depth - 1);
    }
}

public interface IDepthAction
{
    void Run(int depth);
}

public struct ConstrainedPlainWrap<U> : IDepthAction where U : struct
{
    public void Run(int depth) => Functions.PartialConstrainedAttrImpl<ConstrainedPlainWrap<Wrap<U>>>(depth - 1);
}

public struct ConstrainedBarrierWrap<U> : IDepthAction where U : struct
{
    public void Run(int depth)
    {
        if (depth <= 1)
        {
            Gpu.SyncThreads();
        }

        Functions.PartialConstrainedAttrImpl<ConstrainedBarrierWrap<Wrap<U>>>(depth - 1);
    }
}

public partial struct PartialGenStructPoly<T>
{
    public int Value;

    public partial PartialGenStructPoly(int depth);

    public partial PartialGenStructPoly(int depth)
    {
        if (depth <= 0)
        {
            Value = 1;
            return;
        }

        Value = new PartialGenStructPoly<Wrap<T>>(depth - 1).Value;
    }

    [GpuDeviceFunction]
    public static partial void StepPlain(int depth);

    public static partial void StepPlain(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        _ = new PartialGenStructPoly<Wrap<T>>(depth - 1);
    }

    [GpuDeviceFunction]
    public static partial void StepBarrier(int depth);

    public static partial void StepBarrier(int depth)
    {
        if (depth <= 0)
        {
            Gpu.SyncThreads();
            return;
        }

        _ = new PartialGenStructPoly<Wrap<T>>(depth - 1);
    }
}

public partial struct GenBox<T> where T : struct, IAction
{
    [GpuDeviceFunction]
    public static partial void Step(int depth);

    public static partial void Step(int depth)
    {
        if (depth <= 0)
        {
            default(T).Run();
            return;
        }

        Step(depth - 1);
    }
}

public interface IActionGen
{
    void Run<U>() where U : struct, IAction;
}

public struct ImplActionGen : IActionGen
{
    public void Run<U>() where U : struct, IAction => default(U).Run();
}

public interface IActionOfGen<T> where T : struct, IAction
{
    void Run<U>() where U : struct, IAction;
}

public struct ImplActionOfGen<T> : IActionOfGen<T> where T : struct, IAction
{
    public void Run<U>() where U : struct, IAction
    {
        default(T).Run();
        default(U).Run();
    }
}

public struct GenericConstrainedGenMethodCtor<T, U> where T : struct, IActionGen where U : struct, IAction
{
    public int Value;

    public GenericConstrainedGenMethodCtor(int x)
    {
        default(T).Run<U>();
        Value = x;
    }
}

public partial struct PartialGenericContainer<TOuter> where TOuter : struct, IAction
{
    [GpuDeviceFunction]
    public static partial void PartialGeneric<TInner>() where TInner : struct, IAction;

    public static partial void PartialGeneric<TInner>() where TInner : struct, IAction
    {
        default(TOuter).Run();
        default(TInner).Run();
    }
}

public partial struct PartialStructHelperAttrOnImpl
{
    public static partial void PartialHelper<T>();

    [GpuDeviceFunction]
    public static partial void PartialHelper<T>() => Gpu.SyncThreads();
}

public partial struct PartialGenStructAttrOnImpl<T>
{
    public static partial void PartialHelper();

    [GpuDeviceFunction]
    public static partial void PartialHelper() => Gpu.SyncThreads();
}

public interface IAction
{
    void Run();
}

public struct BarrierAction : IAction
{
    public void Run() => Functions.BarrierLeaf(1);
}

public struct PlainAction : IAction
{
    public void Run() => Functions.PlainLeaf(1);
}

public struct CtorBarrierAction
{
    public int X;

    public CtorBarrierAction()
    {
        X = Functions.BarrierLeaf(1);
    }
}

public struct PlainCtorAction
{
    public int X;

    public PlainCtorAction()
    {
        X = Functions.PlainLeaf(1);
    }
}

public struct DisposableBarrierAction : IDisposable
{
    public void Dispose() => Functions.BarrierLeaf(1);
}

public struct PlainDisposableAction : IDisposable
{
    public void Dispose() => Functions.PlainLeaf(1);
}

public struct GenericConstrainedCtor<T> where T : struct, IAction
{
    public int Value;

    public GenericConstrainedCtor(int x)
    {
        default(T).Run();
        Value = x;
    }
}

public struct GenericNewTCtor<T> where T : new()
{
    public int Value;

    public GenericNewTCtor(int x)
    {
        _ = new T();
        Value = x;
    }
}

public struct GenericUsingTCtor<T> where T : struct, IDisposable
{
    public int Value;

    public GenericUsingTCtor(int x)
    {
        using var t = default(T);
        Value = x;
    }
}

public struct Outer<T> where T : struct, IAction
{
    public struct Inner
    {
        public int Value;

        public Inner(int x)
        {
            default(T).Run();
            Value = x;
        }
    }

    public struct Nested<U> where U : struct, IAction
    {
        public int Value;

        public Nested(int x)
        {
            default(T).Run();
            default(U).Run();
            Value = x;
        }
    }

    public struct Mid
    {
        public struct Leaf
        {
            public int Value;

            public Leaf(int x)
            {
                default(T).Run();
                Value = x;
            }
        }
    }
}

public struct GenericBox<T> where T : struct, IAction
{
    public int Prop
    {
        get
        {
            default(T).Run();
            return 1;
        }
        set => default(T).Run();
    }
}

public interface IHasProp
{
    int Prop { set; }
}

public struct ExplicitPropBarrier : IHasProp
{
    public int X;

    int IHasProp.Prop
    {
        set => X = Functions.BarrierLeaf(value);
    }
}

public struct PlainPropStruct : IHasProp
{
    public int X;

    int IHasProp.Prop
    {
        set => X = Functions.PlainLeaf(value);
    }
}

public struct Wrap<T>
{
    public T Value;
}

public struct BarrierCtor
{
    public int Value;

    public BarrierCtor(int x)
    {
        Value = Functions.BarrierLeaf(x);
    }
}

public struct PlainCtor
{
    public int Value;

    public PlainCtor(int x)
    {
        Value = Functions.PlainLeaf(x);
    }
}

public struct GetterBarrierProp
{
    public int X;

    public int Value
    {
        get => Functions.BarrierLeaf(X);
        set => X = value;
    }
}

public struct SetterBarrierProp
{
    public int X;

    public int Value
    {
        get => X;
        set => X = Functions.BarrierLeaf(value);
    }
}

public struct PlainProp
{
    public int X;

    public int Value
    {
        get => Functions.PlainLeaf(X);
        set => X = value;
    }
}

public struct ChainedBarrierCtor
{
    public int Value;

    public ChainedBarrierCtor(int x) : this(x, true)
    {
    }

    public ChainedBarrierCtor(int x, bool _)
    {
        Value = Functions.BarrierLeaf(x);
    }
}

public struct ChainedPlainCtor
{
    public int Value;

    public ChainedPlainCtor(int x) : this(x, true)
    {
    }

    public ChainedPlainCtor(int x, bool _)
    {
        Value = Functions.PlainLeaf(x);
    }
}

public struct FieldInitBarrier
{
    public int Value = Functions.BarrierLeaf(1);

    public FieldInitBarrier()
    {
    }
}

public struct FieldInitPlain
{
    public int Value = Functions.PlainLeaf(1);

    public FieldInitPlain()
    {
    }
}

public struct IndexerBarrier
{
    public int X;

    public int this[int index] => Functions.BarrierLeaf(X + index);
}

public struct IndexerPlain
{
    public int X;

    public int this[int index] => Functions.PlainLeaf(X + index);
}

public struct ConversionBarrier
{
    public int X;

    public static implicit operator int(ConversionBarrier value) => Functions.BarrierLeaf(value.X);
}

public struct ConversionPlain
{
    public int X;

    public static implicit operator int(ConversionPlain value) => Functions.PlainLeaf(value.X);
}

public struct StructWithLocalFunction
{
    public int Value;

    public StructWithLocalFunction(int x)
    {
        int Local(int y) => Functions.BarrierLeaf(y);
        Value = Local(x);
    }
}

[CollectionBuilder(typeof(BarrierBagBuilder), nameof(BarrierBagBuilder.Create))]
public struct BarrierBag
{
    public int First;

    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

public static partial class BarrierBagBuilder
{
    [GpuDeviceFunction]
    public static BarrierBag Create(ReadOnlySpan<int> values)
    {
        Gpu.SyncThreads();
        BarrierBag bag;
        bag.First = values[0];
        return bag;
    }
}

[CollectionBuilder(typeof(PlainBagBuilder), nameof(PlainBagBuilder.Create))]
public struct PlainBag
{
    public int First;

    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

public static partial class PlainBagBuilder
{
    [GpuDeviceFunction]
    public static PlainBag Create(ReadOnlySpan<int> values)
    {
        PlainBag bag;
        bag.First = values[0];
        return bag;
    }
}

public struct FieldInitWithParamCtor
{
    public int Value = Functions.BarrierLeaf(1);

    public FieldInitWithParamCtor(int x)
    {
        Value = x;
    }
}

public record struct FieldInitRecordStruct(int X)
{
    public int Value = Functions.BarrierLeaf(1);
}
