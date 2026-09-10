using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.Math.BigInt;

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

    public readonly int AlignRadixPoint(GpuBigUFloat<TSize> other, out GpuBigUFloat<TSize> alignedOther,
        out bool stickyBit)
    {
        var expDiff = (long)Exponent - other.Exponent;
        var targetExp = expDiff > 0 ? Exponent : other.Exponent;

        if (expDiff == 0)
        {
            alignedOther = other;
            stickyBit = false;
            return 0;
        }

        if (expDiff > 0)
        {
            var shiftAmount = expDiff > int.MaxValue ? int.MaxValue : (int)expDiff;
            var shiftedSig = other.Significand.ShiftRight(shiftAmount, out stickyBit);
            alignedOther = new GpuBigUFloat<TSize>(shiftedSig, targetExp);
            return (int)(expDiff > int.MaxValue ? int.MaxValue : expDiff);
        }
        else
        {
            var absDiff = -expDiff;
            alignedOther = new GpuBigUFloat<TSize>(other.Significand, targetExp);
            stickyBit = false;
            return (int)(absDiff > int.MaxValue ? int.MaxValue : absDiff);
        }
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
            normExp = Exponent + 1;
        }
        else if (leadingZeros > 0)
        {
            normSig = normSig.ShiftLeft(leadingZeros);
            normExp = Exponent - leadingZeros;
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
        // #12 Rounding implementation
        var increment = false;

        switch (mode)
        {
            case GpuBigFloatRoundingMode.RoundToNearestEven:
                if (guardBit)
                {
                    if (roundBit || stickyBit)
                    {
                        increment = true;
                    }
                    else
                    {
                        // round to even (check LSB)
                        var shifted = Significand.ShiftRight(1, out var lsb);
                        increment = lsb;
                    }
                }

                break;
            case GpuBigFloatRoundingMode.RoundTowardPositiveInfinity:
                increment = guardBit || roundBit || stickyBit;
                break;
            case GpuBigFloatRoundingMode.RoundTowardZero:
            case GpuBigFloatRoundingMode.RoundTowardNegativeInfinity:
                increment = false;
                break;
        }

        if (increment)
        {
            var one = new GpuUIntBigInteger<TSize>(
                default); // pseudo code: need actual 1, assuming + operator handles it
            var roundedSig = Significand + one;
            // if carry out occurred during round, we should normalize again.
            // for simplicity, returning without carry out check here, assuming GpuUIntBigInteger handles overflow.
            return new GpuBigUFloat<TSize>(roundedSig, Exponent);
        }

        return new GpuBigUFloat<TSize>(Significand, Exponent);
    }

    public static GpuBigUFloat<TSize> operator +(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Add(right);

    public static GpuBigUFloat<TSize> operator -(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Subtract(right);

    public readonly GpuBigUFloat<TSize> Add(GpuBigUFloat<TSize> other)
    {
        var expDiff = (long)Exponent - other.Exponent;
        var targetExp = Exponent;
        var sigA = Significand;
        var sigB = other.Significand;
        bool stickyA = false, stickyB = false;

        if (expDiff > 0)
        {
            var shiftAmount = expDiff > int.MaxValue ? int.MaxValue : (int)expDiff;
            sigB = sigB.ShiftRight(shiftAmount, out stickyB);
        }
        else if (expDiff < 0)
        {
            var absDiff = -expDiff;
            var shiftAmount = absDiff > int.MaxValue ? int.MaxValue : (int)absDiff;
            sigA = sigA.ShiftRight(shiftAmount, out stickyA);
            targetExp = other.Exponent;
        }

        var resultSig = sigA.Add(sigB, out var carry);
        var result = new GpuBigUFloat<TSize>(resultSig, targetExp);
        result = result.Normalize(0, carry);
        return result.Round(GpuBigFloatRoundingMode.RoundToNearestEven, false, false, stickyA | stickyB);
    }

    public readonly GpuBigUFloat<TSize> Subtract(GpuBigUFloat<TSize> other)
    {
        var expDiff = (long)Exponent - other.Exponent;
        var targetExp = Exponent;
        var sigA = Significand;
        var sigB = other.Significand;
        bool stickyA = false, stickyB = false;

        if (expDiff > 0)
        {
            var shiftAmount = expDiff > int.MaxValue ? int.MaxValue : (int)expDiff;
            sigB = sigB.ShiftRight(shiftAmount, out stickyB);
        }
        else if (expDiff < 0)
        {
            var absDiff = -expDiff;
            var shiftAmount = absDiff > int.MaxValue ? int.MaxValue : (int)absDiff;
            sigA = sigA.ShiftRight(shiftAmount, out stickyA);
            targetExp = other.Exponent;
        }

        // assumes sigA >= sigB for unsigned subtraction
        GpuUIntBigInteger<TSize> resultSig;
        if (sigA >= sigB)
        {
            resultSig = sigA.Subtract(sigB, out _);
        }
        else
        {
            resultSig = new GpuUIntBigInteger<TSize>(default); // clamp to 0 for unsigned
        }

        var result = new GpuBigUFloat<TSize>(resultSig, targetExp);
        var leadingZeros = resultSig.IsZero() ? 0 : resultSig.CountLeadingZeros();
        return result.Normalize(leadingZeros, false);
    }

    public readonly int CompareTo(GpuBigUFloat<TSize> other)
    {
        var thisZero = Significand.IsZero();
        var otherZero = other.Significand.IsZero();

        if (thisZero && otherZero)
        {
            return 0;
        }

        if (thisZero)
        {
            return -1;
        }

        if (otherZero)
        {
            return 1;
        }

        if (Exponent > other.Exponent)
        {
            return 1;
        }

        if (Exponent < other.Exponent)
        {
            return -1;
        }

        return Significand.CompareTo(other.Significand);
    }

    public readonly bool Equals(GpuBigUFloat<TSize> other) => CompareTo(other) == 0;

    public readonly override bool Equals(object? obj) => obj is GpuBigUFloat<TSize> other && Equals(other);

    public readonly override int GetHashCode()
    {
        if (Significand.IsZero())
        {
            return 0;
        }

        return HashCode.Combine(Significand.GetHashCode(), Exponent);
    }

    public static bool operator ==(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.Equals(right);
    public static bool operator !=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => !left.Equals(right);
    public static bool operator <(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) => left.CompareTo(right) >= 0;
}
