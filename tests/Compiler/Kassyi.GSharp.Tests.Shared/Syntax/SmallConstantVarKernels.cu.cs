using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>An enum with a byte base, so that its arithmetic wraps at 256.</summary>
public enum SmallConstantByteEnum : byte
{
    First = 0,
    Last = byte.MaxValue
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
