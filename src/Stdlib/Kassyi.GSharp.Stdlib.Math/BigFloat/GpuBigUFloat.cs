
namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision unsigned floating-point number on the GPU using a fixed-size significand and separate exponent.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the significand storage size.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUFloat<TSize> : IEquatable<GpuBigUFloat<TSize>>, IComparable<GpuBigUFloat<TSize>>
    where TSize : unmanaged
{
    /// <summary>
    /// The significand (mantissa) stored as a fixed-size unsigned big integer.
    /// </summary>
    public GpuUIntBigInteger<TSize> Significand;

    /// <summary>
    /// The binary exponent of the floating-point number.
    /// </summary>
    public int Exponent;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUFloat{TSize}"/> struct.
    /// </summary>
    /// <param name="significand">The significand payload.</param>
    /// <param name="exponent">The binary exponent.</param>
    public GpuBigUFloat(GpuUIntBigInteger<TSize> significand, int exponent)
    {
        Significand = significand;
        Exponent = exponent;
    }

    public readonly (GpuBigUFloat<TSize> AlignedThis, GpuBigUFloat<TSize> AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigUFloat<TSize> other)
    {
        var expDiff = (long)Exponent - other.Exponent;
        var isPositive = expDiff > 0;
        var isNegative = expDiff < 0;

        var absDiff = isPositive ? expDiff : -expDiff;
        var shiftAmount = absDiff > int.MaxValue ? int.MaxValue : (int)absDiff;

        var shiftedOther = other.Significand.ShiftRight(shiftAmount, out var gO, out var rO, out var stickyO);
        var shiftedThis = Significand.ShiftRight(shiftAmount, out var gT, out var rT, out var stickyT);

        var finalThisSig = isNegative ? shiftedThis : Significand;
        var stickyThis = isNegative && stickyT;

        var finalOtherSig = isPositive ? shiftedOther : other.Significand;
        var stickyOther = isPositive && stickyO;

        var guard = isPositive ? gO : (isNegative && gT);
        var round = isPositive ? rO : (isNegative && rT);

        var targetExp = isNegative ? other.Exponent : Exponent;

        var alignedThis = new GpuBigUFloat<TSize>(finalThisSig, targetExp);
        var alignedOther = new GpuBigUFloat<TSize>(finalOtherSig, targetExp);

        return (alignedThis, alignedOther, guard, round, stickyThis, stickyOther);
    }

    public readonly GpuBigUFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false)
    {
        if (Significand.IsZero() && !carryOut)
        {
            return new GpuBigUFloat<TSize>(Significand, 0);
        }

        int normExp;
        var normSig = Significand;

        if (carryOut)
        {
            // overflow: shift right by 1 to normalize
            normSig = normSig.ShiftRight(1, out _);
            normSig = normSig.SetMsb();
            checked
            {
                normExp = Exponent + 1;
            }
        }
        else if (leadingZeros > 0)
        {
            normSig = normSig.ShiftLeft(leadingZeros);
            checked
            {
                normExp = Exponent - leadingZeros;
            }
        }
        else
        {
            normExp = Exponent;
        }

        return new GpuBigUFloat<TSize>(normSig, normExp);
    }

    public readonly GpuBigUFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit,
        bool stickyBit)
    {
        var g = guardBit ? 1u : 0u;
        var r = roundBit ? 1u : 0u;
        var s = stickyBit ? 1u : 0u;
        var isOdd = Significand.IsOdd() ? 1u : 0u;

        var m = (uint)mode;
        var isRne = m == (uint)GpuBigFloatRoundingMode.RoundToNearestEven ? 1u : 0u;
        var isRtpi = m == (uint)GpuBigFloatRoundingMode.RoundTowardPositiveInfinity ? 1u : 0u;

        var incRne = g & (r | s | isOdd);
        var incRtpi = g | r | s;

        var incrementUInt = (isRne & incRne) | (isRtpi & incRtpi);

        var incrementVal = incrementUInt == 1u ? GpuUIntBigInteger<TSize>.One : new GpuUIntBigInteger<TSize>(default);
        var roundedSig = Significand.Add(incrementVal, out var carryOut);
        var result = new GpuBigUFloat<TSize>(roundedSig, Exponent);

        return carryOut ? result.Normalize(0, true) : result;
    }

    public static GpuBigUFloat<TSize> operator +(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Add(right);

    public static GpuBigUFloat<TSize> operator -(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Subtract(right);

    public readonly GpuBigUFloat<TSize> Add(GpuBigUFloat<TSize> other)
    {
        var (alignedThis, alignedOther, guard, round, stickyA, stickyB) = AlignRadixPoint(other);

        var resultSig = alignedThis.Significand.Add(alignedOther.Significand, out var carry);
        var result = new GpuBigUFloat<TSize>(resultSig, alignedThis.Exponent);
        var leadingZeros = carry || resultSig.IsZero() ? 0 : resultSig.CountLeadingZeros();
        result = result.Normalize(leadingZeros, carry);
        return result.Round(GpuBigFloatRoundingMode.RoundToNearestEven, guard, round, stickyA | stickyB);
    }

    public readonly GpuBigUFloat<TSize> Subtract(GpuBigUFloat<TSize> other)
    {
        var (alignedThis, alignedOther, _, _, _, _) = AlignRadixPoint(other);

        // assumes alignedThis.Significand >= alignedOther.Significand for unsigned subtraction
        var isGreaterOrEqual = alignedThis.Significand >= alignedOther.Significand;
        var subSig = alignedThis.Significand.Subtract(alignedOther.Significand, out _);
        var resultSig = isGreaterOrEqual ? subSig : new GpuUIntBigInteger<TSize>(default);

        var result = new GpuBigUFloat<TSize>(resultSig, alignedThis.Exponent);
        var leadingZeros = resultSig.IsZero() ? 0 : resultSig.CountLeadingZeros();
        return result.Normalize(leadingZeros, false);
    }

    public readonly int CompareTo(GpuBigUFloat<TSize> other)
    {
        var thisZero = Significand.IsZero();
        var otherZero = other.Significand.IsZero();

        switch (thisZero)
        {
            case true when otherZero:

                return 0;

            case true:
                return -1;
        }

        if (otherZero)
        {
            return 1;
        }

        var (alignedThis, alignedOther, _, _, _, _) = AlignRadixPoint(other);
        return alignedThis.Significand.CompareTo(alignedOther.Significand);
    }

    public readonly bool Equals(GpuBigUFloat<TSize> other) => CompareTo(other) == 0;

    public readonly override bool Equals(object? obj) => obj is GpuBigUFloat<TSize> other && Equals(other);

    public readonly override int GetHashCode()
    {
        if (Significand.IsZero())
        {
            return 0;
        }

        var normalized = Normalize(Significand.CountLeadingZeros(), false);
        return HashCode.Combine(normalized.Significand.GetHashCode(), normalized.Exponent);
    }

    public static bool operator ==(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.Equals(right);
    public static bool operator !=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => !left.Equals(right);
    public static bool operator <(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) >= 0;
}
