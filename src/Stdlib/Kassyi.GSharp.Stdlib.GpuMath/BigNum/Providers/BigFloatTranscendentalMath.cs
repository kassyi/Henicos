using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Host (<see cref="BigInteger"/>) implementation of the big-float transcendental functions of the CPU provider
/// (<see cref="CSharpCpuBigFloatArithmetic"/>).
/// </summary>
/// <remarks>
/// It takes the same plan (<see cref="TranscendentalPlanner"/>) and the same integer steps, with the same truncations,
/// as the GPU implementation (<see cref="GpuBigFloatTranscendentals"/>), so the two return identical results.
/// </remarks>
internal static class BigFloatTranscendentalMath
{
    /// <summary>
    /// Computes <c>ln x</c> for <c>x = significand · 2^exponent &gt; 0</c> to <paramref name="resultBits"/> bits.
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <returns>
    /// The significand (exactly <paramref name="resultBits"/> bits, or zero for <c>ln 1</c>), exponent and sign of the
    /// result, within one unit of its last bit of <c>ln x</c>.
    /// </returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>x = m·2^e</c> with <c>m ∈ [3/4, 3/2)</c> (<c>e</c> from the bit length and the top two bits), and
    /// <c>ln x = e·ln 2 + 2·y·T(y²)</c>, <c>y = (m − 1) / (m + 1)</c>, <c>|y| ≤ 1/5</c>,
    /// <c>T(z) = Σ_{k&lt;N} z^k / (2k+1)</c> in fixed point with <c>W</c> fraction bits (Horner from the top).
    /// <c>y</c> keeps its own exponent, so an argument close to 1 loses no bits to the fixed point.
    /// </para>
    /// <para>
    /// The <c>W</c>-bit approximation is within <see cref="LogPlan.ErrorBound"/> units of <c>ln x</c>; the result is
    /// that approximation rounded to the nearest <c>p</c>-bit value (ties up), so it is within one unit of <c>ln x</c>.
    /// </para>
    /// </remarks>
    internal static (BigInteger Significand, int Exponent, bool IsNegative) Log(
        BigInteger significand, int exponent, int resultBits)
    {
        var bitLength = (int)significand.GetBitLength();

        // x in [2^(E-1), 2^E). With the top two bits 11 (x >= 1.5 · 2^(E-1)), m = x / 2^E in [3/4, 1); otherwise
        // m = x / 2^(E-1) in [1, 3/2).
        var top = (long)exponent + bitLength;
        var upper = bitLength >= 2 && (significand >> (bitLength - 2)) == 3;
        var e = upper ? top : top - 1;

        // |m - 1| = U / 2^b (upper) or U / 2^(b-1) (lower), |y| = U / D.
        var power = BigInteger.One << (upper ? bitLength : bitLength - 1);
        var reduced = upper ? power - significand : significand - power;
        var denominator = power + significand;
        var reducedBits = (int)reduced.GetBitLength();
        if (reduced.IsZero && e == 0)
        {
            return (BigInteger.Zero, 0, false);
        }

        var closeness = reduced.IsZero
            ? int.MaxValue
            : upper ? bitLength - reducedBits : bitLength - reducedBits - 1;
        var plan = TranscendentalPlanner.PlanLog(resultBits, e, closeness);
        var workingBits = plan.WorkingBits;

        BigInteger approximation;
        long approximationExponent;
        bool negative;
        if (e == 0)
        {
            var series = EvaluateSeries(reduced, denominator, bitLength, reducedBits, plan, out var seriesExponent);
            (approximation, approximationExponent) = Normalize(series, seriesExponent, workingBits);
            negative = upper;
        }
        else
        {
            var fractionBits = workingBits + 2;
            var sum = MultiplyLn2(e, plan.Ln2FractionBits, fractionBits);
            if (!reduced.IsZero)
            {
                var series = EvaluateSeries(reduced, denominator, bitLength, reducedBits, plan, out var seriesExponent);

                // 2|y|T · 2^F; the series exponent 1 - sh - W is below -F.
                var aligned = series >> (int)-(seriesExponent + fractionBits);
                sum = upper == (e < 0) ? sum + aligned : sum - aligned;
            }

            (approximation, approximationExponent) = Normalize(sum, -fractionBits, workingBits);
            negative = e < 0;
        }

        return RoundToNearest(approximation, approximationExponent, workingBits - resultBits, resultBits, negative);
    }

    // 2|y|T as an integer with the exponent 1 - sh - W.
    private static BigInteger EvaluateSeries(
        BigInteger reduced, BigInteger denominator, int bitLength, int reducedBits, in LogPlan plan,
        out long seriesExponent)
    {
        var workingBits = plan.WorkingBits;

        // Y = floor(U · 2^sh / D) in [2^(W-1), 2^(W+1)), |y| = Y · 2^-sh with a relative error below 2^-(W-1).
        var shift = workingBits + bitLength - reducedBits + 1;
        var y = (reduced << shift) / denominator;

        // Z = floor(Y² · 2^(W - 2sh)) = z in W fraction bits.
        var z = (y * y) >> (2 * shift - workingBits);
        var t = AtanhSeriesHorner(z, plan.Terms, workingBits);
        seriesExponent = 1L - shift - workingBits;
        return y * t;
    }

    // H = floor(2^W / (2N - 1)); H = floor(H · Z / 2^W) + floor(2^W / (2k + 1)) for k = N - 2 down to 0.
    private static BigInteger AtanhSeriesHorner(BigInteger z, int terms, int workingBits)
    {
        var one = BigInteger.One << workingBits;
        var h = one / (2 * terms - 1);
        for (var k = terms - 2; k >= 0; k--)
        {
            h = (h * z >> workingBits) + one / (2 * k + 1);
        }

        return h;
    }

    // floor(|e| · L / 2^(Lbits - F)) with L = floor(ln 2 · 2^Lbits), Lbits rounded up to a word as on the GPU.
    private static BigInteger MultiplyLn2(long e, int ln2FractionBits, int fractionBits)
    {
        var ln2Bits = (ln2FractionBits + 31) / 32 * 32;
        var ln2 = GpuBigFloatConstants.GetLn2Fixed(ln2Bits);
        return Math.Abs(e) * ln2 >> (ln2Bits - fractionBits);
    }

    // floor((A + 2^(g-1)) / 2^g) = floor((floor(A / 2^(g-1)) + 1) / 2), normalized to p bits.
    private static (BigInteger, int, bool) RoundToNearest(
        BigInteger approximation, long approximationExponent, int guardBits, int resultBits, bool negative)
    {
        var kept = (approximation >> (guardBits - 1)) + 1;
        var (rounded, roundedExponent) = Normalize(kept, approximationExponent + guardBits - 1, resultBits);
        return (rounded, checked((int)roundedExponent), negative);
    }

    // Shifts a positive value to exactly `bits` bits (truncating), adjusting the exponent.
    private static (BigInteger Value, long Exponent) Normalize(BigInteger value, long exponent, int bits)
    {
        var shift = bits - (int)value.GetBitLength();
        return shift >= 0 ? (value << shift, exponent - shift) : (value >> -shift, exponent - shift);
    }
}
