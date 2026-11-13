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
