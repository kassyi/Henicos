using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the significand storage size.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigFloat<TSize> where TSize : unmanaged
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
    public readonly int AlignRadixPoint(GpuBigFloat<TSize> other, out GpuBigFloat<TSize> alignedOther, out bool stickyBit)
    {
        int expDiff = UFloat.AlignRadixPoint(other.UFloat, out var alignedUFloat, out stickyBit);
        alignedOther = new GpuBigFloat<TSize>(alignedUFloat, other.IsNegative);
        return expDiff;
    }

    /// <summary>
    /// Normalizes the floating-point significand and adjusts the exponent.
    /// </summary>
    /// <param name="leadingZeros">The count of leading zeros to shift out.</param>
    /// <param name="carryOut"><see langword="true"/> if a carry bit was produced during addition.</param>
    /// <returns>The normalized <see cref="GpuBigFloat{TSize}"/>.</returns>
    public readonly GpuBigFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false)
    {
        return new GpuBigFloat<TSize>(UFloat.Normalize(leadingZeros, carryOut), IsNegative);
    }

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
        return new GpuBigFloat<TSize>(UFloat.Round(mode, guardBit, roundBit, stickyBit), IsNegative);
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
        return new GpuBigFloat<TSize>(UFloat.Add(other.UFloat), IsNegative);
    }

    /// <summary>
    /// Subtracts another <see cref="GpuBigFloat{TSize}"/> value from this instance.
    /// </summary>
    public readonly GpuBigFloat<TSize> Subtract(GpuBigFloat<TSize> other)
    {
        return new GpuBigFloat<TSize>(UFloat.Subtract(other.UFloat), IsNegative);
    }
}
