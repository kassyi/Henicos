using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>An enum with a 64-bit unsigned base whose member does not fit 32 bits.</summary>
public enum ConstFieldLiteralWideEnum : ulong
{
    Small = 1,
    Large = 1UL << 40
}

/// <summary>An enum with a signed base whose members sit at the ends of the range.</summary>
public enum ConstFieldLiteralSignedEnum
{
    Lowest = int.MinValue,
    Negative = -5,
    Highest = int.MaxValue
}

/// <summary>A struct whose constructor has defaults that a <c>new</c> in a kernel omits.</summary>
public struct ConstFieldLiteralDefaults
{
    public long First;
    public long Narrow;
    public long Wide;
    public float NaN;

    public ConstFieldLiteralDefaults(int first, int narrow = int.MinValue, long wide = long.MinValue, float nan = float.NaN)
    {
        First = first;
        Narrow = narrow;
        Wide = wide;
        NaN = nan;
    }
}

/// <summary>
/// Kernels that read <c>const</c> fields (and the <c>const</c> fields of the BCL, such as <see cref="int.MinValue"/>).
/// Every reference is written as a C++ literal in place of the field, which must keep the C# value.
/// </summary>
public static partial class ConstFieldLiteralKernels
{
    public const char Zero = '0';
    public const char LetterA = 'A';
    public const char NewLine = '\n';
    public const char EAcute = 'é';
    public const char Nul = '\0';
    public const char Backslash = '\\';
    public const char Quote = '\'';
    public const char LastBmp = '\uFFFF';

    public const float WholeSingle = 2.0f;
    public const float HalfSingle = 1.5f;
    public const float NegativeZeroSingle = -0.0f;

    public const double HalfDouble = 1.5;
    public const double WholeDouble = 2.0;
    public const double HugeDouble = 1e300;
    public const double NegativeZeroDouble = -0.0;

    public const int Negative = -5;
    public const long NegativeLong = -7L;
    public const long OneLong = 1L;
    public const ulong OneULong = 1UL;
    public const uint SevenU = 7U;

    [GpuKernel]
    public static void CharConstKernel(int[] output, char[] text)
    {
        // '0' is 48, not the digit 0
        output[0] = Zero;
        output[1] = LetterA;
        output[2] = NewLine;
        output[3] = EAcute;
        output[4] = Nul;
        output[5] = Backslash;
        output[6] = Quote;
        output[7] = LastBmp;
        text[0] = LetterA;
        text[1] = LastBmp;
    }

    [GpuKernel]
    public static void IntegerLimitConstKernel(long[] signed, ulong[] unsigned, int[] narrow)
    {
        // The C++ literal 2147483648 does not fit int: MSVC reads it as unsigned long, and the minus keeps it positive
        signed[0] = int.MinValue;
        signed[1] = long.MinValue;
        signed[2] = int.MaxValue;
        signed[3] = long.MaxValue;
        unsigned[0] = uint.MaxValue;
        unsigned[1] = ulong.MaxValue;
        narrow[0] = int.MinValue;
        narrow[1] = int.MinValue / 2;
    }

    [GpuKernel]
    public static void EnumConstKernel(ulong[] wide, int[] signed)
    {
        wide[0] = (ulong)ConstFieldLiteralWideEnum.Small;
        wide[1] = (ulong)ConstFieldLiteralWideEnum.Large;
        signed[0] = (int)ConstFieldLiteralSignedEnum.Lowest;
        signed[1] = (int)ConstFieldLiteralSignedEnum.Negative;
        signed[2] = (int)ConstFieldLiteralSignedEnum.Highest;
    }

    [GpuKernel]
    public static void SingleConstKernel(float[] output)
    {
        // C++ needs a decimal point before "f" ("2f" is an integer with an unknown suffix)
        output[0] = WholeSingle;
        output[1] = HalfSingle;
        output[2] = float.Epsilon;
        output[3] = NegativeZeroSingle;
        output[4] = float.MaxValue;
        output[5] = float.NaN;
        output[6] = float.PositiveInfinity;
        output[7] = float.NegativeInfinity;
        output[8] = 3.0f * HalfSingle;
    }

    [GpuKernel]
    public static void DoubleConstKernel(double[] output)
    {
        // C++ has no "d" suffix
        output[0] = HalfDouble;
        output[1] = WholeDouble;
        output[2] = HugeDouble;
        output[3] = double.Epsilon;
        output[4] = NegativeZeroDouble;
        output[5] = double.MaxValue;
        output[6] = double.NaN;
        output[7] = double.PositiveInfinity;
        output[8] = double.NegativeInfinity;
        output[9] = 3.0 * HalfDouble;
    }

    [GpuDeviceFunction]
    public static float ScaleBy(float x, float k = 1f) => x * k;

    [GpuDeviceFunction]
    public static double OffsetBy(double x, double d = 1.0) => x + d;

    [GpuDeviceFunction]
    public static long Widen(int m = int.MinValue) => m;

    [GpuDeviceFunction]
    public static int CodeOf(char c = LetterA) => c;

    // No default clause in the syntax: the call writes the value of [DefaultParameterValue] itself
    [GpuDeviceFunction]
    public static float ScaleByAttribute(float x, [Optional, DefaultParameterValue(1f)] float k) => x * k;

    [GpuDeviceFunction]
    public static double OffsetByAttribute(double x, [Optional, DefaultParameterValue(1.0)] double d) => x + d;

    [GpuDeviceFunction]
    public static long WidenByAttribute([Optional, DefaultParameterValue(int.MinValue)] int m) => m;

    [GpuDeviceFunction]
    public static int CodeOfAttribute([Optional, DefaultParameterValue('A')] char c) => c;

    [GpuDeviceFunction]
    public static float NaNByAttribute([Optional, DefaultParameterValue(float.NaN)] float x) => x;

    [GpuKernel]
    public static void OmittedArgumentKernel(float[] singles, double[] doubles, long[] longs, int[] codes)
    {
        singles[0] = ScaleBy(3f);
        singles[1] = ScaleByAttribute(3f);
        singles[2] = NaNByAttribute();
        doubles[0] = OffsetBy(0.5);
        doubles[1] = OffsetByAttribute(0.5);
        longs[0] = Widen();
        longs[1] = WidenByAttribute();
        codes[0] = CodeOf();
        codes[1] = CodeOfAttribute();
    }

    // The generated launcher repeats these defaults in C#
    [GpuKernel]
    public static void KernelDefaultKernel(
        float[] singles,
        double[] doubles,
        long[] longs,
        int[] codes,
        float k = 1.5f,
        double h = 0.5,
        long n = long.MinValue,
        int m = int.MinValue,
        char c = LetterA,
        float nan = float.NaN)
    {
        singles[0] = k;
        singles[1] = nan;
        doubles[0] = h;
        longs[0] = n;
        longs[1] = m;
        codes[0] = c;
    }

    // An enum default is the base value in metadata; the launcher has to cast it back
    [GpuKernel]
    public static void EnumDefaultKernel(
        ulong[] wide,
        int[] signed,
        ConstFieldLiteralWideEnum w = ConstFieldLiteralWideEnum.Large,
        ConstFieldLiteralSignedEnum s = ConstFieldLiteralSignedEnum.Lowest)
    {
        wide[0] = (ulong)w;
        signed[0] = (int)s;
    }

    // A negative operand right after '-' or '+' must not merge into "--" or "++"
    [GpuKernel]
    public static void NegativeOperandKernel(int[] ints, long[] longs, float[] singles, int[] values)
    {
        int x = values[0];
        ints[0] = x - Negative;
        ints[1] = x - -5;
        ints[2] = - -x;
        ints[3] = x + +x;
        ints[4] = x - -x * 2;
        ints[5] = x - - -x;
        longs[0] = x - NegativeLong;
        singles[0] = -NegativeZeroSingle;
        singles[1] = 1.0f - -NegativeZeroSingle;
    }

    // A negated literal widened to long or double: -2147483648 is int.MinValue in C#
    [GpuKernel]
    public static void NegativeLiteralKernel(long[] longs, double[] doubles)
    {
        longs[0] = -2147483648;
        longs[1] = -9223372036854775808;
        longs[2] = -5;
        doubles[0] = (double)-2147483648;
        doubles[1] = -0.0;
        doubles[2] = -1.5;
    }

    // Without U / LL / ULL the C++ literal has another type: an int that overflows, or a 64-bit product
    [GpuKernel]
    public static void SuffixKernel(ulong[] output, uint factor, int shift)
    {
        output[0] = uint.MaxValue * factor;
        output[1] = (ulong)(OneLong << shift);
        output[2] = OneULong << (shift + 23);
        // var keeps uint in C#, auto keeps the literal's type in C++: 7 - 8 wraps only when the literal is 7U
        var seven = SevenU;
        output[3] = seven - 8;
    }

    [GpuDeviceFunction]
    public static float NullableNaN([Optional, DefaultParameterValue(float.NaN)] float? x) => x.Value;

    [GpuDeviceFunction]
    public static int NullableCodeByAttribute([Optional, DefaultParameterValue('A')] char? c) => c.Value;

    [GpuDeviceFunction]
    public static int NullableCode(char? c = 'A') => c.Value;

    [GpuKernel]
    public static void NullableDefaultKernel(float[] singles, int[] codes)
    {
        singles[0] = NullableNaN();
        codes[0] = NullableCodeByAttribute();
        codes[1] = NullableCode();
    }

    [GpuDeviceFunction]
    public static IEnumerable<long> DefaultValues(int narrow = int.MinValue, long wide = long.MinValue)
    {
        yield return narrow;
        yield return wide;
    }

    // No default clause in the syntax: the inliner builds the argument from the value
    [GpuDeviceFunction]
    public static IEnumerable<long> AttributeDefaultValues(
        [Optional, DefaultParameterValue(int.MinValue)] int narrow,
        [Optional, DefaultParameterValue(long.MinValue)] long wide)
    {
        yield return narrow;
        yield return wide;
    }

    [GpuDeviceFunction]
    public static IEnumerable<double> AttributeNonFiniteValues(
        [Optional, DefaultParameterValue(float.NaN)] float nan,
        [Optional, DefaultParameterValue(float.PositiveInfinity)] float infinity,
        [Optional, DefaultParameterValue(double.NaN)] double doubleNaN,
        [Optional, DefaultParameterValue(double.NegativeInfinity)] double negativeInfinity,
        [Optional] double zero)
    {
        yield return nan;
        yield return infinity;
        yield return doubleNaN;
        yield return negativeInfinity;
        yield return zero;
    }

    // Omitted arguments that the compiler writes itself: a constructor called with new, an iterator expanded inline
    [GpuKernel]
    public static void SynthesizedDefaultKernel(long[] longs, float[] singles, double[] doubles)
    {
        var defaults = new ConstFieldLiteralDefaults(1);
        longs[0] = defaults.First;
        longs[1] = defaults.Narrow;
        longs[2] = defaults.Wide;
        singles[0] = defaults.NaN;
        int k = 3;
        foreach (var value in DefaultValues())
        {
            longs[k] = value;
            k++;
        }

        foreach (var value in AttributeDefaultValues())
        {
            longs[k] = value;
            k++;
        }

        int d = 0;
        foreach (var value in AttributeNonFiniteValues())
        {
            doubles[d] = value;
            d++;
        }
    }

    // The loop variable is a constant in each copy: a negative one follows '-', and labels carry it in their name
    [GpuKernel]
    public static void NegativeUnrollKernel(int[] output, int[] values)
    {
        int x = values[0];
        Gpu.ForUnroll(-2, 4, i =>
        {
            if (i == 0)
            {
                goto skip;
            }

            output[i + 2] = x - i;
            skip:;
        });
    }
}
