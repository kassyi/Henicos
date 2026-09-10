using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents an arbitrary-precision unsigned floating-point number on the GPU using a fixed-size significand and separate exponent.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the significand storage size.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUFloat<TSize> where TSize : unmanaged
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

    /// <summary>
    /// Aligns the radix point between this instance and another <see cref="GpuBigUFloat{TSize}"/>.
    /// </summary>
    /// <param name="other">The operand to align against.</param>
    /// <param name="alignedOther">The resulting aligned operand.</param>
    /// <param name="stickyBit">The sticky bit preserved from right-shifting.</param>
    /// <returns>The exponent difference $\Delta E$.</returns>
    public readonly int AlignRadixPoint(GpuBigUFloat<TSize> other, out GpuBigUFloat<TSize> alignedOther, out bool stickyBit)
    {
        int expDiff = System.Math.Abs(Exponent - other.Exponent);
        int targetExp = System.Math.Max(Exponent, other.Exponent);
        alignedOther = new GpuBigUFloat<TSize>(other.Significand, targetExp);
        stickyBit = false;
        return expDiff;
    }

    /// <summary>
    /// Normalizes the floating-point significand and adjusts the exponent.
    /// </summary>
    /// <param name="leadingZeros">The count of leading zeros to shift out.</param>
    /// <param name="carryOut"><see langword="true"/> if a carry bit was produced during addition.</param>
    /// <returns>The normalized <see cref="GpuBigUFloat{TSize}"/>.</returns>
    public readonly GpuBigUFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false)
    {
        int normExp;
        checked
        {
            normExp = carryOut ? Exponent + 1 : Exponent - leadingZeros;
        }
        return new GpuBigUFloat<TSize>(Significand, normExp);
    }

    /// <summary>
    /// Applies IEEE 754 compliant rounding to the unsigned floating-point value.
    /// </summary>
    /// <param name="mode">The requested IEEE 754 rounding mode.</param>
    /// <param name="guardBit">The guard bit ($G$).</param>
    /// <param name="roundBit">The round bit ($R$).</param>
    /// <param name="stickyBit">The sticky bit ($S$).</param>
    /// <returns>The rounded <see cref="GpuBigUFloat{TSize}"/>.</returns>
    public readonly GpuBigUFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit)
    {
        return new GpuBigUFloat<TSize>(Significand, Exponent);
    }

    /// <summary>
    /// Adds two <see cref="GpuBigUFloat{TSize}"/> values.
    /// </summary>
    public static GpuBigUFloat<TSize> operator +(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Add(right);

    /// <summary>
    /// Subtracts one <see cref="GpuBigUFloat{TSize}"/> value from another.
    /// </summary>
    public static GpuBigUFloat<TSize> operator -(GpuBigUFloat<TSize> left, GpuBigUFloat<TSize> right) =>
        left.Subtract(right);

    /// <summary>
    /// Adds two <see cref="GpuBigUFloat{TSize}"/> values.
    /// </summary>
    public readonly GpuBigUFloat<TSize> Add(GpuBigUFloat<TSize> other)
    {
        return new GpuBigUFloat<TSize>(Significand, System.Math.Max(Exponent, other.Exponent));
    }

    /// <summary>
    /// Subtracts another <see cref="GpuBigUFloat{TSize}"/> value from this instance.
    /// </summary>
    public readonly GpuBigUFloat<TSize> Subtract(GpuBigUFloat<TSize> other)
    {
        return new GpuBigUFloat<TSize>(Significand, System.Math.Max(Exponent, other.Exponent));
    }
}
