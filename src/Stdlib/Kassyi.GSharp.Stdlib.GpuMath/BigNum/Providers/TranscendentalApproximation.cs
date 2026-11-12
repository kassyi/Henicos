namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// The approximation of a transcendental function value before it is rounded to the result precision.
/// </summary>
/// <param name="Significand">The significand <c>A</c>; zero only for an exact zero (<c>ln 1</c>).</param>
/// <param name="Exponent">The exponent of <paramref name="Significand"/>.</param>
/// <param name="IsNegative">The sign of the value.</param>
/// <param name="ErrorBound">
/// The bound <c>B</c>: the exact value lies within <c>B·2^UnitExponent</c> of <c>±A·2^Exponent</c>.
/// </param>
/// <param name="WorkingBits">The working precision <c>W</c>; the unit is the last bit of <c>A</c> normalized to <c>W</c> bits.</param>
internal readonly record struct TranscendentalApproximation(
    BigInteger Significand, long Exponent, bool IsNegative, BigInteger ErrorBound, int WorkingBits)
{
    /// <summary>Gets the exponent of the unit of <see cref="ErrorBound"/> (the last bit of <c>A</c> at <c>W</c> bits).</summary>
    public long UnitExponent => Exponent + (long)Significand.GetBitLength() - WorkingBits;
}
