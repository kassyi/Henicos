using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace SpillSuppressionTest
{
    // The kernels without the pragma reach each Via* function in one way only; MarkedKernel calls them all directly, so
    // each one stays reported exactly when the walk sees that way

    public struct Holder
    {
        public int V;

        public int Doubled => Kernels.ViaGetter(V);

        public int Tripled
        {
            get => V;
            set => V = Kernels.ViaSetter(value);
        }

        public int this[int i] => Kernels.ViaIndexer(V + i);

        public static implicit operator int(Holder holder) => Kernels.ViaConversion(holder.V);
    }

    public struct Holder2
    {
        public int V;

        public int Value => Kernels.ViaNested(V);
    }

    public struct Built
    {
        public int V;

        public Built(int v)
        {
            V = Kernels.ViaConstructor(v);
        }
    }

    public struct Initialized
    {
        public int A = Kernels.ViaInitializer(3);
        public int B;

        public Initialized(int b)
        {
            B = b;
        }
    }

    public struct WithParameterless
    {
        public int A;

        public WithParameterless()
        {
            A = Kernels.ViaGenericNew(1);
        }
    }

    public struct GenericHolder<T>() where T : struct
    {
        public T Value = new T();
    }

    public ref struct Disposer
    {
        public int A;

        public void Dispose()
        {
            A = Kernels.ViaDispose(A);
        }
    }

    // A generic struct that constructs itself once more with a constant type argument: finite (Grower<int> and
    // Grower<Wrapper<int>>), and new T() of the second runs Wrapper<int>(), which calls ViaFiniteGrowth
    public struct Wrapper<T> where T : struct
    {
        public int A;

        public Wrapper()
        {
            A = Kernels.ViaFiniteGrowth(1);
        }
    }

    public struct Grower<T> where T : struct
    {
        public T Value;
        public int N;

        public Grower(int n)
        {
            Value = new T();
            N = n;
            if (n > 0)
            {
                N += new Grower<Wrapper<int>>(n - 1).N;
            }
        }
    }

    // Forth<T> creates Back<int> (a constant), and Back<U> creates Forth<IndirectWrapper<U>>: an expansive call, but on
    // no cycle, so the instantiations are finite (Forth<int>, Back<int>, Forth<IndirectWrapper<int>>); new T() of the
    // last runs IndirectWrapper<int>(), which calls ViaIndirectGrowth
    public struct IndirectWrapper<T> where T : struct
    {
        public int A;

        public IndirectWrapper()
        {
            A = Kernels.ViaIndirectGrowth(1);
        }
    }

    public struct Forth<T> where T : struct
    {
        public T Value;
        public int N;

        public Forth(int n)
        {
            Value = new T();
            N = n;
            if (n > 0)
            {
                N += new Back<int>(n - 1).N;
            }
        }
    }

    public struct Back<U> where U : struct
    {
        public int N;

        public Back(int n)
        {
            N = n;
            if (n > 0)
            {
                N += new Forth<IndirectWrapper<U>>(n - 1).N;
            }
        }
    }

    // A member of an interface called on a constrained type parameter: the call runs the implementation of the
    // type argument (HasImpl.Value), which no body names
    public interface IHasValue
    {
        int Value { get; }
    }

    public struct HasImpl : IHasValue
    {
        public int A;

        public int Value => Kernels.ViaConstrainedGetter(A);
    }

    public struct ReadsValue<T> where T : struct, IHasValue
    {
        public int N;

        public ReadsValue(int n)
        {
            N = default(T).Value + n;
        }
    }

    public interface IHasSlot
    {
        int Value { get; set; }

        int this[int i] { get; }

        ref int Slot { get; }
    }

    public struct SlotImpl : IHasSlot
    {
        public int[] Data;
        public int A;

        public int Value
        {
            get => A;
            set => A = Kernels.ViaConstrainedSetter(value);
        }

        public int this[int i] => Kernels.ViaConstrainedIndexer(A + i);

        public ref int Slot => ref Data[Kernels.ViaConstrainedRef(0)];
    }

    public struct WritesValue<T> where T : struct, IHasSlot
    {
        public int N;

        public WritesValue(int n)
        {
            var value = default(T);
            value.Value = n;
            N = n;
        }
    }

    public struct ReadsIndexer<T> where T : struct, IHasSlot
    {
        public int N;

        public ReadsIndexer(int n)
        {
            N = default(T)[n];
        }
    }

    public struct ReadsSlot<T> where T : struct, IHasSlot
    {
        public int N;

        public ReadsSlot(T value)
        {
            N = value.Slot;
        }
    }

    public ref struct DeclarationDisposer
    {
        public int A;

        public void Dispose()
        {
            A = Kernels.ViaUsingDeclaration(A);
        }
    }

    public ref struct ExpressionDisposer
    {
        public int A;

        public void Dispose()
        {
            A = Kernels.ViaUsingExpression(A);
        }
    }

    [CollectionBuilder(typeof(Kernels), nameof(Kernels.CreateBag))]
    public struct Bag
    {
        public int Sum;

        public readonly IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
    }

    public struct Window
    {
        public int[] Data;
        public int Offset;
    }

    public struct MacroValue
    {
        public int Value;

        [GpuOperatorMacro(typeof(MacroOps), nameof(MacroOps.Add))]
        public static MacroValue operator +(MacroValue left, MacroValue right) => throw new GpuOnlyException();
    }

    // && tests its left operand with operator false, which the expansion calls as a function of its own
    public struct Truth
    {
        public int Value;

        [GpuOperatorMacro(typeof(MacroOps), nameof(MacroOps.And))]
        public static Truth operator &(Truth left, Truth right) => throw new GpuOnlyException();

        [GpuOperatorMacro(typeof(MacroOps), nameof(MacroOps.Or))]
        public static Truth operator |(Truth left, Truth right) => throw new GpuOnlyException();

        public static bool operator true(Truth value) => value.Value > 0;

        public static bool operator false(Truth value) => Kernels.ViaTruthTest(value.Value) < 0;
    }

    public static partial class MacroOps
    {
        [GpuDeviceFunction]
        public static void Add(ref MacroValue result, MacroValue left, MacroValue right)
        {
            result.Value = Kernels.ViaMacro(left.Value) + right.Value;
        }

        [GpuDeviceFunction]
        public static void And(ref Truth result, Truth left, Truth right)
        {
            result.Value = left.Value * 10 + right.Value;
        }

        [GpuDeviceFunction]
        public static void Or(ref Truth result, Truth left, Truth right)
        {
            result.Value = left.Value * 100 + right.Value;
        }
    }

    // Reached only from MarkedKernel: a constructor and a conversion, which are named as functions of their own
    public struct OnlyMarked
    {
        public int V;

        public OnlyMarked(int v)
        {
            V = v;
        }
    }

    public struct OnlyMarkedConversion
    {
        public int V;

        public static implicit operator int(OnlyMarkedConversion value) => value.V + 1;
    }

    public static unsafe partial class Kernels
    {
        // Reached only from MarkedKernel, through Own
        [GpuDeviceFunction]
        public static int Leaf(int x) => x + 1;

        [GpuDeviceFunction]
        public static int Own(int x) => Leaf(x) * 3;

        // Reached from MarkedKernel and from UnmarkedKernel
        [GpuDeviceFunction]
        public static int Shared(int x) => x * 2;

        // Recursive, reached only from UnmarkedKernel
        [GpuDeviceFunction]
        public static int Countdown(int x) => x <= 0 ? 0 : Countdown(x - 1) + 1;

        [GpuDeviceFunction] public static int ViaGetter(int x) => x + 1;
        [GpuDeviceFunction] public static int ViaSetter(int x) => x + 2;
        [GpuDeviceFunction] public static int ViaIndexer(int x) => x + 3;
        [GpuDeviceFunction] public static int ViaConversion(int x) => x + 4;
        [GpuDeviceFunction] public static int ViaMacro(int x) => x + 5;
        [GpuDeviceFunction] public static int ViaTruthTest(int x) => x + 6;
        [GpuDeviceFunction] public static int ViaConstructor(int x) => x + 7;
        [GpuDeviceFunction] public static int ViaInitializer(int x) => x + 8;
        [GpuDeviceFunction] public static int ViaGenericNew(int x) => x + 9;
        [GpuDeviceFunction] public static int ViaLocal(int x) => x + 10;
        [GpuDeviceFunction] public static int ViaLambda(int x) => x + 11;
        [GpuDeviceFunction] public static int ViaMethodGroup(int x) => x + 12;
        [GpuDeviceFunction] public static int ViaDispose(int x) => x + 13;
        [GpuDeviceFunction] public static int ViaBuilder(int x) => x + 14;
        [GpuDeviceFunction] public static int ViaPinnable(int x) => x + 15;
        [GpuDeviceFunction] public static int ViaNested(int x) => x + 16;
        [GpuDeviceFunction] public static int ViaUsingDeclaration(int x) => x + 17;
        [GpuDeviceFunction] public static int ViaUsingExpression(int x) => x + 18;
        [GpuDeviceFunction] public static int ViaFiniteGrowth(int x) => x + 19;
        [GpuDeviceFunction] public static int ViaIndirectGrowth(int x) => x + 20;
        [GpuDeviceFunction] public static int ViaConstrainedGetter(int x) => x + 21;
        [GpuDeviceFunction] public static int ViaConstrainedSetter(int x) => x + 22;
        [GpuDeviceFunction] public static int ViaConstrainedIndexer(int x) => x + 23;
        [GpuDeviceFunction] public static int ViaConstrainedRef(int x) => x + 24;

        // A device function whose own call of ViaNested is not an invocation (a property getter)
        [GpuDeviceFunction]
        public static int Relay(int x) => new Holder2 { V = x }.Value;

        [GpuDeviceFunction]
        public static Bag CreateBag(ReadOnlySpan<int> values)
        {
            var bag = new Bag();
            bag.Sum = ViaBuilder(values[0]);
            return bag;
        }

        [GpuDeviceFunction]
        public static ref int GetPinnableReference(this Window window)
        {
            return ref window.Data[ViaPinnable(window.Offset)];
        }

#pragma warning disable GS0001
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernel(int[] d)
        {
            d[0] = Own(d[0]) + Shared(d[1]) + new OnlyMarked(d[0]).V;
            int x = new OnlyMarkedConversion { V = d[1] };
            d[2] = x + ViaGetter(d[1]) + ViaSetter(d[1]) + ViaIndexer(d[1]) + ViaConversion(d[1]) + ViaMacro(d[1]) +
                ViaTruthTest(d[1]) + ViaConstructor(d[1]) + ViaInitializer(d[1]) + ViaGenericNew(d[1]) +
                ViaLocal(d[1]) + ViaLambda(d[1]) + ViaMethodGroup(d[1]) + ViaDispose(d[1]) + ViaBuilder(d[1]) +
                ViaPinnable(d[1]) + ViaNested(d[1]) + ViaUsingDeclaration(d[1]) + ViaUsingExpression(d[1]) +
                ViaFiniteGrowth(d[1]) + ViaIndirectGrowth(d[1]) +
                ViaConstrainedGetter(d[1]) + ViaConstrainedSetter(d[1]) + ViaConstrainedIndexer(d[1]) +
                ViaConstrainedRef(d[1]);
        }
#pragma warning restore GS0001

        // Its name starts with the name of the marked kernel
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernelTwin(int[] d)
        {
            d[0] = d[1];
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedKernel(int[] d)
        {
            d[0] = Shared(d[1]) + Countdown(d[2]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedGetterKernel(int[] d)
        {
            d[0] = new Holder { V = d[1] }.Doubled;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedSetterKernel(int[] d)
        {
            var holder = new Holder();
            holder.Tripled = d[1];
            d[0] = holder.V;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedIndexerKernel(int[] d)
        {
            var holder = new Holder { V = d[1] };
            d[0] = holder[0];
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConversionKernel(int[] d)
        {
            int x = new Holder { V = d[1] };
            d[0] = x;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedMacroKernel(int[] d)
        {
            MacroValue a = new MacroValue { Value = d[1] };
            MacroValue b = new MacroValue { Value = 1 };
            MacroValue c = default;
            c = a + b;
            d[0] = c.Value;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedTruthTestKernel(int[] d)
        {
            Truth a = new Truth { Value = d[1] };
            Truth b = new Truth { Value = d[2] };
            Truth c = default;
            c = a && b;
            d[0] = c.Value;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstructorKernel(int[] d)
        {
            d[0] = new Built(d[1]).V;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedInitializerKernel(int[] d)
        {
            d[0] = new Initialized(d[1]).A;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedGenericNewKernel(int[] d)
        {
            var holder = new GenericHolder<WithParameterless>();
            d[0] = holder.Value.A;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedLocalFunctionKernel(int[] d)
        {
            d[0] = Local(d[1]);

            static int Local(int x) => ViaLocal(x);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedLambdaKernel(int[] d)
        {
            Func<int, int> f = x => ViaLambda(x);
            d[0] = f(d[1]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedMethodGroupKernel(int[] d)
        {
            Func<int, int> f = ViaMethodGroup;
            d[0] = f(d[1]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedUsingKernel(int[] d)
        {
            using (var resource = new Disposer { A = d[1] })
            {
                d[0] = resource.A;
            }
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedUsingDeclarationKernel(int[] d)
        {
            using var resource = new DeclarationDisposer { A = d[1] };
            d[0] = resource.A;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedUsingExpressionKernel(int[] d)
        {
            var resource = new ExpressionDisposer { A = d[1] };
            using (resource)
            {
                d[0] = resource.A;
            }
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedFiniteGrowthKernel(int[] d)
        {
            d[0] = new Grower<int>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedIndirectGrowthKernel(int[] d)
        {
            d[0] = new Forth<int>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstrainedGetterKernel(int[] d)
        {
            d[0] = new ReadsValue<HasImpl>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstrainedSetterKernel(int[] d)
        {
            d[0] = new WritesValue<SlotImpl>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstrainedIndexerKernel(int[] d)
        {
            d[0] = new ReadsIndexer<SlotImpl>(d[1]).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstrainedRefKernel(int[] d)
        {
            var slot = new SlotImpl { Data = d };
            d[0] = new ReadsSlot<SlotImpl>(slot).N;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedBuilderKernel(int[] d)
        {
            Bag bag = [d[1], 2];
            d[0] = bag.Sum;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedPinnableKernel(int[] d)
        {
            var window = new Window { Data = d, Offset = 0 };
            fixed (int* p = window)
            {
                *p = 1;
            }
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedNestedKernel(int[] d)
        {
            d[0] = Relay(d[1]);
        }
    }
}
