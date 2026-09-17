using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the significand storage size.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigFloat<TSize> : IEquatable<GpuBigFloat<TSize>>, IComparable<GpuBigFloat<TSize>>
    where TSize : unmanaged
{
    /// <summary>
    /// The underlying unsigned floating-point value payload.
    /// </summary>
    public GpuBigUFloat<TSize> UFloat;

    /// <summary>
    /// Gets or sets a value indicating whether the floating-point number is negative.
    /// </summary>
    public bool IsNegative;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat{TSize}"/> struct.
    /// </summary>
    /// <param name="uFloat">The unsigned float component.</param>
    /// <param name="isNegative"><see langword="true"/> if negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat(GpuBigUFloat<TSize> uFloat, bool isNegative)
    {
        UFloat = uFloat;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloat{TSize}"/> struct from significand, exponent, and sign flag.
    /// </summary>
    /// <param name="significand">The significand payload.</param>
    /// <param name="exponent">The binary exponent.</param>
    /// <param name="isNegative"><see langword="true"/> if negative; otherwise, <see langword="false"/>.</param>
    public GpuBigFloat(GpuUIntBigInteger<TSize> significand, int exponent, bool isNegative)
        : this(new GpuBigUFloat<TSize>(significand, exponent), isNegative)
    {
    }

    /// <summary>
    /// Aligns the radix point between this instance and another <see cref="GpuBigFloat{TSize}"/>.
    /// </summary>
    /// <param name="other">The operand to align against.</param>
    /// <param name="alignedOther">The resulting aligned operand.</param>
    /// <param name="stickyBit">The sticky bit preserved from right-shifting.</param>
    /// <returns>The exponent difference $\Delta E$.</returns>
    public readonly void AlignRadixPoint(GpuBigFloat<TSize> other, out GpuBigFloat<TSize> alignedThis,
        out GpuBigFloat<TSize> alignedOther, out bool stickyThis, out bool stickyOther)
    {
        UFloat.AlignRadixPoint(other.UFloat, out var alignedUThis, out var alignedUOther, out stickyThis,
            out stickyOther);
        alignedThis = new GpuBigFloat<TSize>(alignedUThis, IsNegative);
        alignedOther = new GpuBigFloat<TSize>(alignedUOther, other.IsNegative);
    }

    /// <summary>
    /// Normalizes the floating-point significand and adjusts the exponent.
    /// </summary>
    /// <param name="leadingZeros">The count of leading zeros to shift out.</param>
    /// <param name="carryOut"><see langword="true"/> if a carry bit was produced during addition.</param>
    /// <returns>The normalized <see cref="GpuBigFloat{TSize}"/>.</returns>
    public readonly GpuBigFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false) => new(UFloat.Normalize(leadingZeros, carryOut), IsNegative);

    /// <summary>
    /// Applies IEEE 754 compliant rounding to the floating-point value.
    /// </summary>
    /// <param name="mode">The requested IEEE 754 rounding mode.</param>
    /// <param name="guardBit">The guard bit ($G$).</param>
    /// <param name="roundBit">The round bit ($R$).</param>
    /// <param name="stickyBit">The sticky bit ($S$).</param>
    /// <returns>The rounded <see cref="GpuBigFloat{TSize}"/>.</returns>
    public readonly GpuBigFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit)
    {
        var effectiveMode = mode;
        if (IsNegative)
        {
            if (mode == GpuBigFloatRoundingMode.RoundTowardPositiveInfinity)
            {
                effectiveMode = GpuBigFloatRoundingMode.RoundTowardNegativeInfinity;
            }
            else if (mode == GpuBigFloatRoundingMode.RoundTowardNegativeInfinity)
            {
                effectiveMode = GpuBigFloatRoundingMode.RoundTowardPositiveInfinity;
            }
        }

        return new GpuBigFloat<TSize>(UFloat.Round(effectiveMode, guardBit, roundBit, stickyBit), IsNegative);
    }

    /// <summary>
    /// Adds two <see cref="GpuBigFloat{TSize}"/> values.
    /// </summary>
    public static GpuBigFloat<TSize> operator +(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) =>
        left.Add(right);

    /// <summary>
    /// Subtracts one <see cref="GpuBigFloat{TSize}"/> value from another.
    /// </summary>
    public static GpuBigFloat<TSize> operator -(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) =>
        left.Subtract(right);

    /// <summary>
    /// Adds two <see cref="GpuBigFloat{TSize}"/> values.
    /// </summary>
    public readonly GpuBigFloat<TSize> Add(GpuBigFloat<TSize> other)
    {
        if (IsNegative == other.IsNegative)
        {
            return new GpuBigFloat<TSize>(UFloat.Add(other.UFloat), IsNegative);
        }
        else
        {
            return SubtractMagnitude(this, other);
        }
    }

    public readonly GpuBigFloat<TSize> Subtract(GpuBigFloat<TSize> other)
    {
        if (IsNegative != other.IsNegative)
        {
            return new GpuBigFloat<TSize>(UFloat.Add(other.UFloat), IsNegative);
        }
        else
        {
            return SubtractMagnitude(this, other);
        }
    }

    private static GpuBigFloat<TSize> SubtractMagnitude(GpuBigFloat<TSize> a, GpuBigFloat<TSize> b)
    {
        var cmp = a.UFloat.CompareTo(b.UFloat);
        if (cmp > 0)
        {
            return new GpuBigFloat<TSize>(a.UFloat.Subtract(b.UFloat), a.IsNegative);
        }
        else if (cmp < 0)
        {
            return new GpuBigFloat<TSize>(b.UFloat.Subtract(a.UFloat), !a.IsNegative);
        }
        else
        {
            return new GpuBigFloat<TSize>(new GpuBigUFloat<TSize>(new GpuUIntBigInteger<TSize>(default), 0), false);
        }
    }


    public readonly int CompareTo(GpuBigFloat<TSize> other)
    {
        var thisZero = UFloat.Significand.IsZero();
        var otherZero = other.UFloat.Significand.IsZero();

        if (thisZero && otherZero)
        {
            return 0;
        }

        if (thisZero)
        {
            return other.IsNegative ? 1 : -1;
        }

        if (otherZero)
        {
            return IsNegative ? -1 : 1;
        }

        if (IsNegative != other.IsNegative)
        {
            return IsNegative ? -1 : 1;
        }

        var magCmp = UFloat.CompareTo(other.UFloat);
        return IsNegative ? -magCmp : magCmp;
    }

    public readonly bool Equals(GpuBigFloat<TSize> other) => CompareTo(other) == 0;

    public readonly override bool Equals(object? obj) => obj is GpuBigFloat<TSize> other && Equals(other);

    public readonly override int GetHashCode()
    {
        if (UFloat.Significand.IsZero())
        {
            return 0;
        }

        var normalized = UFloat.Normalize(UFloat.Significand.CountLeadingZeros(), false);
        return HashCode.Combine(normalized.Significand.GetHashCode(), normalized.Exponent, IsNegative);
    }

    public static bool operator ==(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => left.Equals(right);
    public static bool operator !=(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => !left.Equals(right);
    public static bool operator <(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => left.CompareTo(right) < 0;
    public static bool operator >(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => left.CompareTo(right) > 0;
    public static bool operator <=(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GpuBigFloat<TSize> left, GpuBigFloat<TSize> right) => left.CompareTo(right) >= 0;
}
