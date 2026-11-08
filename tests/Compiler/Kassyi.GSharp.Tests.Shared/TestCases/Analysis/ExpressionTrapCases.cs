// The body of each method of Traps is the expression that ExpressionTrapsTests classifies; the bodies of Chains are the
// fused StackLinq chains whose range stages it counts
using Kassyi.ZeroAlloc.Linq;

namespace ExpressionTrapCases;

public struct Pair
{
    public int X;

    public int Sum => X + 1;
}

public static class Traps
{
    private static int Twice(int x) => x * 2;

    // --- Trap free ---

    public static int Arithmetic(int x) => (x + 1) * 3 - (x >> 2) ^ ~x;

    public static int ConstantDivisor(int x) => x / 2 + x % 7;

    public static long ConstantLongDivisor(long x) => x / 3L;

    public static float FloatDivision(float x, float y) => x / y;

    public static double FloatMath(double x) => System.Math.Sqrt(x) + System.Math.Max(x, 1.0);

    public static int Conversion(double x) => (int)x;

    public static int Choice(int x) => x > 0 ? x : -x;

    public static int Field(Pair p) => p.X;

    public static int Length(int[] data) => data.Length;

    // --- May trap ---

    public static int VariableDivisor(int x) => 10 / x;

    public static int VariableRemainder(int x, int y) => x % y;

    public static int MinusOneDivisor(int x) => x / -1;

    public static int ComputedDivisor(int x) => x / (x - x);

    public static int Element(int[] table, int x) => table[x];

    public static int CheckedAdd(int x) => checked(x + 1);

    public static int CheckedConversion(long x) => checked((int)x);

    public static int Call(int x) => Twice(x);

    public static int IntegerMath(int x) => System.Math.Abs(x);

    public static int ComputedProperty(Pair p) => p.Sum;

    public static int Throw(int x) => x > 0 ? x : throw new System.InvalidOperationException();
}

public static class Chains
{
    public static int PureSelectThenSkip(int[] data) => data.AsStackEnumerable().Select(x => x / 2).Skip(1).Sum();

    public static int DividingSelectThenSkip(int[] data) => data.AsStackEnumerable().Select(x => 10 / x).Skip(1).Sum();

    public static int IndexingSelectThenSkip(int[] data, int[] table) =>
        data.AsStackEnumerable().Select(x => table[x]).Skip(1).Sum();
}
