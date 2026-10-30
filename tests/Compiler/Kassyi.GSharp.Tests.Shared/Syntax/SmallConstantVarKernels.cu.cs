using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>An enum with a byte base, so that its arithmetic wraps at 256.</summary>
public enum SmallConstantByteEnum : byte
{
    First = 0,
    Last = byte.MaxValue
}

/// <summary>An enum with an sbyte base and a negative member, whose case label needs a sign.</summary>
public enum SmallConstantSignedEnum : sbyte
{
    Negative = -1,
    Positive = 1
}

/// <summary>An enum with a short base, so that its arithmetic wraps at 32768.</summary>
public enum SmallConstantShortEnum : short
{
    Lowest = short.MinValue,
    Highest = short.MaxValue
}

/// <summary>
/// Kernels that let <c>var</c> take its type from a <c>const</c> field of a type narrower than <c>int</c> (#859). C#
/// keeps the field's type, so <c>++</c> and <c>--</c> wrap at its range; the C++ <c>auto</c> takes the type of the
/// literal the reference is written as, which must therefore be the C++ type of the field, not <c>int</c>.
/// </summary>
public static partial class SmallConstantVarKernels
{
    public const byte ByteMax = byte.MaxValue;
    public const sbyte SByteMin = sbyte.MinValue;
    public const sbyte SByteMax = sbyte.MaxValue;
    public const short ShortMax = short.MaxValue;
    public const short ShortMin = short.MinValue;
    public const ushort UShortMax = ushort.MaxValue;
    public const ushort UShortZero = 0;

    [GpuKernel]
    public static void IncrementKernel(int[] output)
    {
        var b = ByteMax;
        b++;
        output[0] = b;

        var sbMax = SByteMax;
        sbMax++;
        output[1] = sbMax;

        var sbMin = SByteMin;
        sbMin--;
        output[2] = sbMin;

        var sMax = ShortMax;
        sMax++;
        output[3] = sMax;

        var sMin = ShortMin;
        --sMin;
        output[4] = sMin;

        var usMax = UShortMax;
        ++usMax;
        output[5] = usMax;

        var usZero = UShortZero;
        usZero--;
        output[6] = usZero;

        // Compound assignment converts back to the variable's type in both languages
        var compound = ByteMax;
        compound += 2;
        output[7] = compound;
    }

    [GpuKernel]
    public static void EnumIncrementKernel(int[] output)
    {
        var last = SmallConstantByteEnum.Last;
        last++;
        output[0] = (int)last;

        var highest = SmallConstantShortEnum.Highest;
        highest++;
        output[1] = (int)highest;

        var lowest = SmallConstantShortEnum.Lowest;
        lowest--;
        output[2] = (int)lowest;
    }

    public const int Negative = -1;

    /// <summary>The results that <see cref="NarrowConstantUses"/> writes.</summary>
    public const int NarrowUseCount = 19;

    /// <summary>
    /// The other places a narrow constant is used, where C# converts it (an <c>int</c> argument, a mixed conditional) or
    /// where its value names a switch label: <c>input</c> holds the switch values (0 to 3, 8 and 9), the <see cref="Math"/>
    /// values (4 and 5), a short (6) and a selector (7).
    /// </summary>
    [GpuDeviceFunction]
    public static void NarrowConstantUses(int[] input, int[] output)
    {
        // A switch over a byte-based enum, with goto case
        switch ((SmallConstantByteEnum)input[0])
        {
            case SmallConstantByteEnum.First:
                output[0] = 10;
                goto case SmallConstantByteEnum.Last;
            case SmallConstantByteEnum.Last:
                output[1] = 11;
                break;
            default:
                output[1] = 12;
                break;
        }

        // A narrow constant, a negative constant and a char as case values
        switch ((byte)input[1])
        {
            case ByteMax:
                output[2] = 20;
                break;
            default:
                output[2] = 21;
                break;
        }

        switch (input[2])
        {
            case Negative:
                output[3] = 30;
                goto case 1;
            case 1:
                output[4] = 31;
                break;
        }

        switch ((char)input[3])
        {
            case 'a':
                output[5] = 40;
                goto case 'b';
            case 'b':
                output[6] = 41;
                break;
        }

        // C# converts the constant to int and calls the int overload
        output[7] = Math.Max(ByteMax, input[4]);
        output[8] = Math.Min(ShortMin, input[4]);
        output[9] = Math.Clamp(input[5], 0, ByteMax);

        // c ? ByteMax : shortValue is a short, which wraps at 32768
        short shortValue = (short)input[6];
        shortValue++;
        var mixedTrue = input[7] > 0 ? ByteMax : shortValue;
        mixedTrue++;
        output[10] = mixedTrue;
        var mixedFalse = input[7] < 0 ? ByteMax : shortValue;
        mixedFalse++;
        output[11] = mixedFalse;

        // A byte and an int parameter
        output[12] = TakeByte(ByteMax);
        output[13] = TakeInt(ByteMax);

        // A short parameter: ByteMax is converted to short, which the body steps to its end and past it
        output[14] = TakeShort(ByteMax);

        // A bool switch with goto case
        switch (input[8] > 0)
        {
            case true:
                output[15] = 50;
                goto case false;
            case false:
                output[16] += 51;
                break;
        }

        // An enum with a negative member, with goto case
        switch ((SmallConstantSignedEnum)input[9])
        {
            case SmallConstantSignedEnum.Negative:
                output[17] = 60;
                goto case SmallConstantSignedEnum.Positive;
            case SmallConstantSignedEnum.Positive:
                output[18] += 61;
                break;
        }
    }

    [GpuDeviceFunction]
    public static int TakeByte(byte value)
    {
        value++;
        return value;
    }

    [GpuDeviceFunction]
    public static int TakeInt(int value) => value + 1;

    // value is 255 as a short: 255 + 32512 is short.MaxValue, and the step wraps to short.MinValue (an int gives 32768)
    [GpuDeviceFunction]
    public static int TakeShort(short value)
    {
        value += 32512;
        value++;
        return value;
    }

    [GpuKernel]
    public static void NarrowConstantUsesKernel(int[] input, int[] output)
    {
        NarrowConstantUses(input, output);
    }

    // A const local keeps its typed declaration; a var that reads it takes that type
    [GpuKernel]
    public static void LocalConstKernel(int[] output)
    {
        const byte localMax = 255;
        var x = localMax;
        x++;
        output[0] = x;
    }
}
