// The body of each method of Divisions is the division or compound assignment whose form IntegerDivisionPlanTests checks:
// a Prelude function with a divisor check, or the C++ operator where the divisor cannot be 0 (or -1 for a dividend
// that may be the minimum of its type)
namespace IntegerDivisionCases;

public static class Divisions
{
    // --- A Prelude function ---

    public static int VariableDivisor(int x, int y) => x / y;

    public static int VariableRemainder(int x, int y) => x % y;

    public static uint UnsignedDivisor(uint x, uint y) => x / y;

    public static long LongDivisor(long x, long y) => x / y;

    public static ulong UnsignedLongRemainder(ulong x, ulong y) => x % y;

    // A byte divisor may be 0
    public static int ByteDivisor(int x, byte y) => x / y;

    // An int dividend may be int.MinValue, which x86 idiv cannot divide by -1
    public static int MinusOneDivisor(int x) => x / -1;

    public static long LongMinusOneRemainder(long x) => x % -1L;

    public static int CompoundDivision(int x, int y) => x /= y;

    public static short NarrowCompoundRemainder(short x, short y) => x %= y;

    // --- The C++ operator ---

    public static int ConstantDivisor(int x) => x / 7;

    public static long ConstantLongRemainder(long x) => x % 1000L;

    public static uint UnsignedConstantDivisor(uint x) => x / 3u;

    // A short dividend is never int.MinValue
    public static int NarrowDividendByMinusOne(short x) => x / -1;

    // uint / int is a long division whose dividend is never long.MinValue
    public static long UnsignedDividendByMinusOne(uint x) => x / -1;

    public static int CompoundConstantDivision(int x) => x /= 4;

    // --- The managed form (ManagedDivision) only ---

    // A property target is read and written once, as the compound assignment does
    public static int PropertyCompound(int y) => Holder.Value /= y;

    // The Length of an array is never negative, so it is never -1 (it may be 0)
    public static int LengthDivisor(int x, int[] a) => x % a.Length;

    // The value writes the target's variable before the division, which the call by reference would read after
    public static int CompoundWithWritingValue(int x, int y) => x /= (y = -1);
}

public static class Holder
{
    public static int Value { get; set; }
}
