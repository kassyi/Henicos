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
