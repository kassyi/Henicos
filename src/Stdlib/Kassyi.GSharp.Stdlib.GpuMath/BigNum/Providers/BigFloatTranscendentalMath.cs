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

        var (rounded, roundedExponent) =
            RoundToNearest(approximation, approximationExponent, workingBits - resultBits, resultBits);
        return (rounded, checked((int)roundedExponent), negative);
    }

    /// <summary>
    /// Computes <c>e^x</c> for <c>x = ±significand · 2^exponent</c> to <paramref name="resultBits"/> bits.
    /// </summary>
    /// <param name="significand">The significand of the argument; zero or positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="negative">Whether the argument is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>
    /// The significand (exactly <paramref name="resultBits"/> bits) and exponent of the result, within one unit of its
    /// last bit of <c>e^x</c>.
    /// </returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/> (an underflow,
    /// named in the message, for a negative argument).</exception>
    /// <remarks>
    /// <para>
    /// <c>x = k·ln 2 + r</c> with <c>k = floor(x / ln 2) − 1</c> from a double approximation, so <c>r ∈ (0.69, 1.39)</c>
    /// for either sign of <c>x</c>. <c>r</c> is formed in fixed point with ln 2 at <c>W − s + bits(|k|) + 2</c> fraction
    /// bits, and read as <c>r / 2^s</c> with <c>W</c> fraction bits. The Taylor polynomial of degree <c>N</c> runs as the
    /// term recurrence, the sum is squared <c>s</c> times, and <c>e^x = e^r · 2^k</c>.
    /// </para>
    /// <para>
    /// <c>|x| ≥ 2^32</c> throws before any work. For <c>|x| &lt; 2^-(p+1)</c> the result is <c>RZ(e^x)</c> directly: 1, or
    /// <c>1 − 2^-p</c> for a negative <c>x</c>.
    /// </para>
    /// </remarks>
    internal static (BigInteger Significand, int Exponent) Exp(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options)
    {
        if (significand.IsZero)
        {
            return (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        var bitLength = (int)significand.GetBitLength();

        // |x| < 2^top.
        var top = (long)exponent + bitLength;
        if (top > 32)
        {
            throw TranscendentalPlanner.ExpOutOfRange(negative);
        }

        if (top <= -(resultBits + 2))
        {
            return negative ? ((BigInteger.One << resultBits) - 1, -resultBits) : (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        var leading = bitLength >= 64
            ? (ulong)(significand >> (bitLength - 64))
            : (ulong)(significand << (64 - bitLength));
        var plan = TranscendentalPlanner.PlanExp(resultBits, ApproximateArgument(leading, top, negative), options);
        var workingBits = plan.WorkingBits;

        // r = x - k·ln 2 with Lw fraction bits (Lw: ln 2 bits rounded up to a word as on the GPU), then truncated once
        // to W - s fraction bits, which is R = r / 2^s with W fraction bits.
        var ln2Bits = (plan.Ln2FractionBits + 31) / 32 * 32;
        var shift = checked(exponent + ln2Bits);
        var x = shift >= 0 ? significand << shift : significand >> -shift;
        var kLn2 = Math.Abs(plan.Reduction) * GpuBigFloatConstants.GetLn2Fixed(ln2Bits);
        var reduced = negative ? kLn2 - x : plan.Reduction >= 0 ? x - kLn2 : x + kLn2;
        var r = reduced >> (ln2Bits - (workingBits - plan.Halvings));

        var q = ExpSeriesRecurrence(r, plan.Terms, workingBits);
        for (var i = 0; i < plan.Halvings; i++)
        {
            q = q * q >> workingBits;
        }

        // e^x = Q · 2^(k - W); Q < 2^(W+3) fits W + 32 bits without truncation.
        var (approximation, approximationExponent) = Normalize(q, plan.Reduction - workingBits, workingBits + 32);
        var (rounded, roundedExponent) =
            RoundToNearest(approximation, approximationExponent, workingBits + 32 - resultBits, resultBits);
        return (rounded, TranscendentalPlanner.ToExpResultExponent(roundedExponent));
    }

    /// <summary>
    /// Returns <c>±leading · 2^(top − 64)</c> as a double: the argument to a relative error below <c>2^-52</c>.
    /// </summary>
    internal static double ApproximateArgument(ulong leading, long top, bool negative)
    {
        var magnitude = Math.ScaleB(leading, (int)(top - 64));
        return negative ? -magnitude : magnitude;
    }

    // T_0 = 2^W, T_j = floor(floor(T_(j-1) · R / 2^W) / j), P = Σ_{j<=N} T_j.
    private static BigInteger ExpSeriesRecurrence(BigInteger r, int terms, int workingBits)
    {
        var term = BigInteger.One << workingBits;
        var sum = term;
        for (var j = 1; j <= terms; j++)
        {
            term = (term * r >> workingBits) / j;
            sum += term;
        }

        return sum;
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
    private static (BigInteger Value, long Exponent) RoundToNearest(
        BigInteger approximation, long approximationExponent, int guardBits, int resultBits)
    {
        var kept = (approximation >> (guardBits - 1)) + 1;
        return Normalize(kept, approximationExponent + guardBits - 1, resultBits);
    }

    // Shifts a positive value to exactly `bits` bits (truncating), adjusting the exponent.
    private static (BigInteger Value, long Exponent) Normalize(BigInteger value, long exponent, int bits)
    {
        var shift = bits - (int)value.GetBitLength();
        return shift >= 0 ? (value << shift, exponent - shift) : (value >> -shift, exponent - shift);
    }
}
