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

    // A block dimension is 1 to 1024: neither 0 nor -1
    public static int LaunchDimensionDivisor(int x) => x % Kassyi.GSharp.Core.BlockDimension.X;

    // The value writes another variable (y), not the target: the call by reference reads x after the value, which
    // changes nothing
    public static int CompoundWithWritingValue(int x, int y) => x /= (y = -1);

    // The value writes the target local: x is read as the first argument, before the value
    public static int CompoundWritingItsLocal(int x) => x /= (x = -1);

    // The value writes the target element: the location is found once and read before the value
    public static int CompoundWritingItsElement(int[] a) => a[0] /= (a[0] = -1);

    // An indexer whose argument writes (k++) would be evaluated twice by the get and the set
    public static int IndexerWithWritingArgument(Grid g, int k) => g[k++] /= -1;

    public static int IndexerWithRepeatableArgument(Grid g, int k) => g[k] /= -1;

    // The receiver of a property of a value type may throw (the element) or be written by the value: it is found once
    public static int ElementPropertyCompound(Cell[] s, int d) => s[0].P /= d;

    public static int PropertyWritingItsReceiver(Cell h, Cell h2) => h.P /= (h = h2).P;

    // The receiver of a property of a reference type is the object it holds when the compound assignment starts
    public static int ObjectPropertyCompound(int d) => Box.Make().Q /= d;
}

public struct Cell
{
    public int P { get; set; }
}

public sealed class Box
{
    public int Q { get; set; }

    public static Box Make() => new();
}

public static class Holder
{
    public static int Value { get; set; }
}

public struct Grid
{
    private int _a;

    public int this[int i]
    {
        get => _a + i;
        set => _a = value - i;
    }
}
