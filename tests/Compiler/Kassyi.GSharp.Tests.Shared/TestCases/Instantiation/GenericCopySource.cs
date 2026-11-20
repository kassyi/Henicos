using System;
using System.Numerics;

namespace App
{
    public struct Vec : IAdditionOperators<Vec, Vec, Vec>
    {
        public int X;

        public static Vec operator +(Vec a, Vec b) => new Vec { X = a.X + b.X };
    }

    // Implements + explicitly and has an instance Ext() that a copy for Odd would bind instead of the extension
    public struct Odd : IAdditionOperators<Odd, Odd, Odd>
    {
        public int X;

        static Odd IAdditionOperators<Odd, Odd, Odd>.operator +(Odd a, Odd b) => a;

        public int Ext() => 1;
    }

    public interface IFoo
    {
        int M();
    }

    public interface IDefault
    {
        int M() => 3;
    }

    // The public M and the explicit IFoo.M differ: x.M() through the constraint runs the explicit one
    public struct Both : IFoo
    {
        public int M() => 1;

        int IFoo.M() => 2;
    }

    public struct Plain : IFoo
    {
        public int M() => 5;
    }

    public struct Defaulted : IDefault
    {
    }

    public interface IStaticGen
    {
        static abstract int Create<U>(U value);
    }

    public struct StaticGenExplicit : IStaticGen
    {
        static int IStaticGen.Create<U>(U value) => 1;
    }

    public interface IInstGen
    {
        int Pick<U>(U value);
    }

    public struct InstGenExplicit : IInstGen
    {
        int IInstGen.Pick<U>(U value) => 1;
    }

    public interface IOverloaded
    {
        int Pick(object value);
    }

    public struct OverloadedTarget : IOverloaded
    {
        public int Pick(object value) => 1;

        public int Pick(int value) => 2;
    }

    public interface ICounter
    {
        int Next();

        void Add(int amount, ref int total);

        int Plus(int value);
    }

    // The members that change the struct are explicit: a call on a copy of it would not show
    public struct Counter : ICounter
    {
        public int Count;

        int ICounter.Next() => ++Count;

        void ICounter.Add(int amount, ref int total)
        {
            Count += amount;
            total += Count;
        }

        public int Plus(int value) => value;

        int ICounter.Plus(int value) => Count + value;
    }

    public static class Exts
    {
        public static int Ext<T>(this T value) => 2;
    }

    public static partial class Kernels
    {
        public static int Helper<U>(U value) => 1;
        public static int Helper(int value) => 2;

        public static int CallsHelper<T>(T s) => Helper(s);

        public static int CallsExt<T>(T[] d) => d[0].Ext();

        public static int Over(object value) => 1;
        public static int Over(int value) => 2;

        // The generic method binds Over(object); the copy for int would bind Over(int)
        public static int CallsOver<T>(T value) => Over(value);

        // The generic method has no member of T to call; the copy for a value type has no ?. to write
        public static string? NullConditional<T>(T value) => value?.ToString();

        public static T Sum<T>(T a, T b) where T : IAdditionOperators<T, T, T> => a + b;

        public static T CheckedSum<T>(T a, T b) where T : IAdditionOperators<T, T, T> => checked(a + b);

        public static T Identities<T>() where T : INumberBase<T> => T.Zero + T.One + T.AdditiveIdentity;

        public static T Max<T>(T a, T b) where T : INumber<T> => T.Max(a, b);

        public static string Name<T>() => nameof(T);

        public static T Negate<T>(T a) where T : IUnaryNegationOperators<T, T> => -a;

        public static T CheckedNegate<T>(T a) where T : IUnaryNegationOperators<T, T> => checked(-a);

        public static T Shift<T>(T a, int n) where T : IShiftOperators<T, int, T> => a << n;

        public static T ShiftRight<T>(T a, int n) where T : IShiftOperators<T, int, T> => a >> n;

        public static T ShiftRightUnsigned<T>(T a, int n) where T : IShiftOperators<T, int, T> => a >>> n;

        public static T CheckedDivide<T>(T a, T b) where T : IDivisionOperators<T, T, T> => checked(a / b);

        public static T CheckedMultiply<T>(T a, T b) where T : IMultiplyOperators<T, T, T> => checked(a * b);

        public static T Complement<T>(T a) where T : IBitwiseOperators<T, T, T> => ~a;

        public static T ShiftAssign<T>(T a, int n) where T : IShiftOperators<T, int, T>
        {
            a <<= n;
            return a;
        }

        public static T MinValue<T>() where T : IMinMaxValue<T> => T.MinValue;

        public static T MaxValue<T>() where T : IMinMaxValue<T> => T.MaxValue;

        // Operators on a constant that C# folds: the copy keeps the low bits of what C# computes in int, as the
        // generic method does, and a plain cast of the folded value would not compile (CS0221)
        public static T ShiftedMaxValue<T>() where T : IShiftOperators<T, int, T>, IMinMaxValue<T> => T.MaxValue << 1;

        public static T NegatedMaxValue<T>() where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T> => -T.MaxValue;

        public static T Pi<T>() where T : IFloatingPointConstants<T> => T.Pi;

        public static T Abs<T>(T a) where T : INumberBase<T> => T.Abs(a);

        public static T MaxMagnitude<T>(T a, T b) where T : INumberBase<T> => T.MaxMagnitude(a, b);

        public static bool IsNaN<T>(T a) where T : INumberBase<T> => T.IsNaN(a);

        public static int Sign<T>(T a) where T : INumber<T> => T.Sign(a);

        public static T Min<T>(T a, T b) where T : INumber<T> => T.Min(a, b);

        public static T OneOverZero<T>() where T : INumberBase<T> => T.One / T.Zero;

        public static T CheckedMaxValuePlusOne<T>() where T : INumberBase<T>, IMinMaxValue<T> =>
            checked(T.MaxValue + T.One);

        public static T CompoundDivide<T>(T a, T b) where T : IDivisionOperators<T, T, T>
        {
            a /= b;
            return a;
        }

        public static T CheckedCompoundDivide<T>(T a, T b) where T : IDivisionOperators<T, T, T>
        {
            checked
            {
                a /= b;
            }

            return a;
        }

        public static T CheckedCompoundAdd<T>(T a, T b) where T : IAdditionOperators<T, T, T>
        {
            checked
            {
                a += b;
            }

            return a;
        }

        public static T ShiftAssignRightUnsigned<T>(T a, int n) where T : IShiftOperators<T, int, T>
        {
            a >>>= n;
            return a;
        }

        public static T Increment<T>(T a) where T : IIncrementOperators<T>
        {
            a++;
            return a;
        }

        public static T CheckedIncrement<T>(T a) where T : IIncrementOperators<T>
        {
            checked
            {
                a++;
            }

            return a;
        }

        public static int CompareTo<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);

        public static T CompoundDivideAt<T>(T[] data, int i, T b) where T : IDivisionOperators<T, T, T>
        {
            data[i] /= b;
            return data[i];
        }

        public static T CompoundDivideWithEffect<T>(T[] data, int i, T b) where T : IDivisionOperators<T, T, T>
        {
            data[i++] /= b;
            return data[0];
        }

        public static int CallFoo<T>(T x) where T : IFoo => x.M();

        public static int CallFooOfValue<T>(T x) where T : IFoo => Same(x).M();

        private static T Same<T>(T value) => value;

        // The calls change x itself: 1 and then 2
        public static int NextTwice<T>(T x) where T : ICounter
        {
            x.Next();
            return x.Next();
        }

        public static int AddAndPlus<T>(T x, int amount) where T : ICounter
        {
            var total = 0;
            x.Add(amount, ref total);
            x.Add(amount, ref total);
            return total * 100 + x.Plus(7);
        }

        public static int NextInArray<T>(T[] items) where T : ICounter
        {
            items[0].Next();
            return items[0].Next() + items[1].Next();
        }

        public static int CallDefault<T>(T x) where T : IDefault => x.M();

        public static T[] Locals<T>(T[] data) where T : unmanaged
        {
            var copy = data;
            T first = copy[0];
            Func<T, T> same = x => x;
            copy[0] = same(first);
            return copy;
        }

        public static int Recurse<T>(int n) where T : unmanaged => n <= 0 ? 0 : Recurse<T>(n - 1);

        public static int CallOverloaded<T>(T x) where T : IOverloaded => x.Pick(1);

        public static string ConcatWith<T>(string left, T right) => left + right;

        public static bool Less<T>(T a, T b) where T : IComparisonOperators<T, T, bool> => a < b;

        public static T MinMagnitude<T>(T a, T b) where T : INumberBase<T> => T.MinMagnitude(a, b);

        public static T AbsPlusB<T>(T a, T b) where T : INumber<T> => T.Abs(a) + b;

        public static bool AbsLessB<T>(T a, T b) where T : INumber<T> => T.Abs(a) < b;

        public static T CompoundAddAbs<T>(T a, T b) where T : INumber<T>
        {
            a += T.Abs(b);
            return a;
        }

        public static int SignOfAbs<T>(T a) where T : INumber<T> => T.Sign(T.Abs(a));

        public static T MaxOfAbs<T>(T a, T b) where T : INumber<T> => T.Max(T.Abs(a), b);

        public static int CallStaticGen<T>(double d) where T : IStaticGen => T.Create(d);

        public static int CallInstGen<T>(T x, double d) where T : IInstGen => x.Pick(d);

        public static T AbsOfAbs<T>(T a) where T : INumberBase<T> => T.Abs(T.Abs(a));

        public static T AbsOfSum<T>(T a, T b) where T : INumberBase<T> => T.Abs(a + b);

        public static T AbsOfNegation<T>(T a) where T : INumberBase<T>, IUnaryNegationOperators<T, T> => T.Abs(-a);

        public static T AbsOfMaxValue<T>() where T : INumberBase<T>, IMinMaxValue<T> => T.Abs(T.MaxValue);

        public static T AbsOfAbsPlusB<T>(T a, T b) where T : INumberBase<T> => T.Abs(T.Abs(a) + b);

        public static T AbsOfMax<T>(T a, T b) where T : INumber<T> => T.Abs(T.Max(a, b));

        public static T CheckedNegateMinValue<T>() where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T> =>
            checked(-T.MinValue);

        public static T NegateMinValue<T>() where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T> => -T.MinValue;

        public static T CheckedNegateParenthesizedMinValue<T>()
            where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T> => checked(-(T.MinValue));

        public static T CheckedNegateUncheckedMinValue<T>()
            where T : IUnaryNegationOperators<T, T>, IMinMaxValue<T> => checked(-unchecked(T.MinValue));

        public static T CheckedNegateOne<T>()
            where T : IUnaryNegationOperators<T, T>, INumberBase<T> => checked(-T.One);

        public static T MinValueOverNegatedOne<T>()
            where T : IDivisionOperators<T, T, T>, IUnaryNegationOperators<T, T>, IMinMaxValue<T>, INumberBase<T> =>
            T.MinValue / -T.One;

        public static T CheckedMinValueTimesNegatedOne<T>()
            where T : IMultiplyOperators<T, T, T>, IUnaryNegationOperators<T, T>, IMinMaxValue<T>, INumberBase<T> =>
            checked(T.MinValue * -T.One);

        public static T CheckedNegatedMaxValueMinusTwo<T>()
            where T : ISubtractionOperators<T, T, T>, IUnaryNegationOperators<T, T>, IMinMaxValue<T>, INumberBase<T> =>
            checked(-T.MaxValue - T.One - T.One);

        public static T CheckedNegateComplementMaxValue<T>()
            where T : IUnaryNegationOperators<T, T>, IBitwiseOperators<T, T, T>, IMinMaxValue<T> =>
            checked(-~T.MaxValue);

        public static T CheckedNegateUnaryPlusMinValue<T>()
            where T : IUnaryNegationOperators<T, T>, IUnaryPlusOperators<T, T>, IMinMaxValue<T> =>
            checked(-+T.MinValue);

        public static T CheckedComplementMaxValueMinusOne<T>()
            where T : ISubtractionOperators<T, T, T>, IBitwiseOperators<T, T, T>, IMinMaxValue<T>, INumberBase<T> =>
            checked(~T.MaxValue - T.One);

        public static T OneModuloComplementMaxValue<T>()
            where T : IModulusOperators<T, T, T>, IBitwiseOperators<T, T, T>, IMinMaxValue<T>, INumberBase<T> =>
            T.One % ~T.MaxValue;

        // Waits at a barrier: the lowering of its copy keeps 'kept' across the wait, in a frame field of the copy's
        // type
        [Kassyi.GSharp.Core.GpuDeviceFunction]
        public static T AfterSync<T>(T value) where T : unmanaged
        {
            T kept = value;
            Kassyi.GSharp.Core.Gpu.SyncThreads();
            return kept;
        }

        // An operator macro whose target takes a context: the context is looked up in the scope of the copy
        public static void ContextMacro<T>(ref MacroState ctx, uint[] left, uint[] right, uint[] result, T tag)
        {
            MacroValue a = left;
            MacroValue b = right;
            MacroValue c = result;
            c = a + b;
        }

        // Locals that are not zeroed: the copy reads the attribute from its own declaration
        [System.Runtime.CompilerServices.SkipLocalsInit]
        public static T SkipLocals<T>(T value)
        {
            T kept = value;
            return kept;
        }
    }

    public struct MacroState
    {
        public int State;
    }

    public static class MacroTargets
    {
        public static void Add(ref MacroState ctx, uint[] result, uint[] left, uint[] right)
        {
        }
    }

    public readonly ref struct MacroValue
    {
        public readonly uint[] Data;

        public MacroValue(uint[] data) => Data = data;

        [Kassyi.GSharp.Core.GpuOperatorMacro(typeof(MacroTargets), nameof(MacroTargets.Add))]
        public static MacroValue operator +(MacroValue a, MacroValue b) => throw new InvalidOperationException();

        public static implicit operator MacroValue(uint[] data) => new(data);
    }

    public static partial class Outer<T> where T : unmanaged
    {
        public static partial class Kernels
        {
            public static int Sibling(T value) => 1;

            public static int Run(T value) => Sibling(value);
        }
    }
}
