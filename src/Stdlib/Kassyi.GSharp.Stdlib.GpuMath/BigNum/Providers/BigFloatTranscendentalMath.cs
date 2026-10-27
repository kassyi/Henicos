using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

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
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>
    /// The significand (exactly <paramref name="resultBits"/> bits, or zero for <c>ln 1</c>), exponent and sign of the
    /// result, within one unit of its last bit of <c>ln x</c>.
    /// </returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static (BigInteger Significand, int Exponent, bool IsNegative) Log(
        BigInteger significand, int exponent, int resultBits, GpuBigFloatTranscendentalOptions options)
    {
        var approximation = LogApproximation(significand, exponent, resultBits, options);
        if (approximation.Significand.IsZero)
        {
            return (BigInteger.Zero, 0, false);
        }

        var (rounded, roundedExponent) = RoundToNearest(approximation, resultBits);
        return (rounded, checked((int)roundedExponent), approximation.IsNegative);
    }

    /// <summary>
    /// Computes the <c>W</c>-bit approximation of <c>ln x</c>, <c>x = significand · 2^exponent &gt; 0</c>, and its bound.
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>The approximation, exactly <c>W</c> bits (zero for <c>ln 1</c>), with <see cref="LogPlan.ErrorBound"/>.</returns>
    /// <remarks>
    /// <para>
    /// <c>x = m·2^e</c> with <c>m ∈ [3/4, 3/2)</c> (<c>e</c> from the bit length and the top two bits), and
    /// <c>ln x = e·ln 2 + 2·y·T(y²)</c>, <c>y = (m − 1) / (m + 1)</c>, <c>|y| ≤ 1/5</c>,
    /// <c>T(z) = Σ_{k&lt;N} z^k / (2k+1)</c> in fixed point with <c>W</c> fraction bits (Horner from the top for a short
    /// series, Paterson–Stockmeyer otherwise). <c>y</c> keeps its own exponent, so an argument close to 1 loses no bits
    /// to the fixed point.
    /// </para>
    /// <para>
    /// For <c>e ≠ 0</c> the sum <c>|e|·L ± 2|y|T</c> is formed with <c>W + 2</c> fraction bits and normalized to
    /// <c>W</c> bits. The bound counts each truncation once (design §4.3.1).
    /// </para>
    /// </remarks>
    internal static TranscendentalApproximation LogApproximation(
        BigInteger significand, int exponent, int resultBits, GpuBigFloatTranscendentalOptions options)
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
            return new TranscendentalApproximation(BigInteger.Zero, 0, false, BigInteger.Zero, 1);
        }

        var closeness = reduced.IsZero
            ? int.MaxValue
            : upper ? bitLength - reducedBits : bitLength - reducedBits - 1;
        var plan = TranscendentalPlanner.PlanLog(resultBits, e, closeness, options);
        var workingBits = plan.WorkingBits;

        BigInteger approximation;
        long approximationExponent;
        bool negative;
        if (e == 0)
        {
            var series = EvaluateLogSeries(reduced, denominator, bitLength, reducedBits, plan, out var seriesExponent);
            (approximation, approximationExponent) = Normalize(series, seriesExponent, workingBits);
            negative = upper;
        }
        else
        {
            var fractionBits = workingBits + 2;
            var sum = MultiplyLn2(e, plan.Ln2FractionBits, fractionBits);
            if (!reduced.IsZero)
            {
                var series = EvaluateLogSeries(reduced, denominator, bitLength, reducedBits, plan, out var seriesExponent);

                // 2|y|T · 2^F; the series exponent 1 - sh - W is below -F.
                var aligned = series >> (int)-(seriesExponent + fractionBits);
                sum = upper == (e < 0) ? sum + aligned : sum - aligned;
            }

            (approximation, approximationExponent) = Normalize(sum, -fractionBits, workingBits);
            negative = e < 0;
        }

        return new TranscendentalApproximation(approximation, approximationExponent, negative, plan.ErrorBound, workingBits);
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
    /// <c>|x| ≥ 2^32</c> throws before any work. For <c>|x| &lt; 2^-(p+1)</c> the result is <c>RZ(e^x)</c> directly: 1, or
    /// <c>1 − 2^-p</c> for a negative <c>x</c>. Every other argument goes through <see cref="ExpApproximation"/>.
    /// </remarks>
    internal static (BigInteger Significand, int Exponent) Exp(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options)
    {
        if (significand.IsZero)
        {
            return (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        // |x| < 2^top.
        var top = (long)exponent + (long)significand.GetBitLength();
        if (top > 32)
        {
            throw TranscendentalPlanner.ExpOutOfRange(negative);
        }

        if (top <= -(resultBits + 2))
        {
            return negative ? ((BigInteger.One << resultBits) - 1, -resultBits) : (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        var approximation = ExpApproximation(significand, exponent, negative, resultBits, options);
        var (rounded, roundedExponent) = RoundToNearest(approximation, resultBits);
        return (rounded, TranscendentalPlanner.ToExpResultExponent(roundedExponent));
    }

    /// <summary>
    /// Computes the approximation of <c>e^x</c>, <c>x = ±significand · 2^exponent</c>, and its bound, for an argument
    /// outside the shortcuts of <see cref="Exp"/> (<c>2^-(p+2) ≤ |x| &lt; 2^32</c>).
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="negative">Whether the argument is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>The approximation <c>Q·2^(k−W)</c>, normalized exactly to <c>W + 32</c> bits, with <see cref="ExpPlan.ErrorBound"/>.</returns>
    /// <exception cref="OverflowException">The planner finds the result exponent out of range.</exception>
    /// <remarks>
    /// <c>x = k·ln 2 + r</c> with <c>k = floor(x / ln 2) − 1</c> from a double approximation, so <c>r ∈ (0.69, 1.39)</c>
    /// for either sign of <c>x</c>. <c>r</c> is formed with ln 2 at <c>Lw</c> fraction bits (<c>W − s + bits(|k|) + 2</c>
    /// rounded up to a word) and truncated once to <c>W − s</c> fraction bits, which is <c>R = r / 2^s</c> with <c>W</c>
    /// fraction bits (<c>|δ| &lt; 1.5·2^-W</c>, design §4.4.1). The Taylor polynomial of degree <c>N</c> runs in
    /// Paterson–Stockmeyer form with Smith's division order, the sum is squared <c>s</c> times, and
    /// <c>e^x = e^r · 2^k</c>.
    /// </remarks>
    internal static TranscendentalApproximation ExpApproximation(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options)
    {
        var bitLength = (int)significand.GetBitLength();
        var top = (long)exponent + bitLength;
        var leading = bitLength >= 64
            ? (ulong)(significand >> (bitLength - 64))
            : (ulong)(significand << (64 - bitLength));
        var plan = TranscendentalPlanner.PlanExp(resultBits, ApproximateArgument(leading, top, negative), options);
        var workingBits = plan.WorkingBits;

        var ln2Bits = (plan.Ln2FractionBits + 31) / 32 * 32;
        var shift = checked(exponent + ln2Bits);
        var x = shift >= 0 ? significand << shift : significand >> -shift;
        var kLn2 = Math.Abs(plan.Reduction) * GpuBigFloatConstants.GetLn2Fixed(ln2Bits);
        var reducedArgument = negative ? kLn2 - x : plan.Reduction >= 0 ? x - kLn2 : x + kLn2;
        var r = reducedArgument >> (ln2Bits - (workingBits - plan.Halvings));

        var q = ExpSeriesPatersonStockmeyer(r, plan.Terms, plan.BlockSize, workingBits);
        for (var i = 0; i < plan.Halvings; i++)
        {
            q = q * q >> workingBits;
        }

        // e^x = Q · 2^(k - W); Q < 2^(W+3) fits W + 32 bits without truncation.
        var (approximation, approximationExponent) = Normalize(q, plan.Reduction - workingBits, workingBits + 32);
        return new TranscendentalApproximation(approximation, approximationExponent, false, plan.ErrorBound, workingBits);
    }

    /// <summary>
    /// Returns <c>±leading · 2^(top − 64)</c> as a double: the argument to a relative error below <c>2^-52</c>.
    /// </summary>
    internal static double ApproximateArgument(ulong leading, long top, bool negative)
    {
        var magnitude = Math.ScaleB(leading, (int)Math.Clamp(top - 64, int.MinValue, int.MaxValue));
        return negative ? -magnitude : magnitude;
    }

    // Σ_{j<=N} R^j / j! in Paterson–Stockmeyer form with Smith's division order (§10.1): powers R_0 = 2^W .. R_m;
    // the top block [a, N] starts from R_(N-a); for i from its top down to 1: acc = floor(acc / (a + i)) + R_(i-1);
    // every lower block [a, a + m) starts from acc = floor(acc · R_m / 2^W) and runs i = m down to 1 the same way.
    private static BigInteger ExpSeriesPatersonStockmeyer(BigInteger r, int terms, int blockSize, int workingBits)
    {
        var powers = Powers(r, blockSize, workingBits);
        var top = terms / blockSize * blockSize;
        var accumulator = powers[terms - top];
        for (var i = terms - top; i >= 1; i--)
        {
            accumulator = accumulator / (top + i) + powers[i - 1];
        }

        for (var first = top - blockSize; first >= 0; first -= blockSize)
        {
            accumulator = accumulator * powers[blockSize] >> workingBits;
            for (var i = blockSize; i >= 1; i--)
            {
                accumulator = accumulator / (first + i) + powers[i - 1];
            }
        }

        return accumulator;
    }

    // 2|y|T as an integer with the exponent 1 - sh - W.
    private static BigInteger EvaluateLogSeries(
        BigInteger reduced, BigInteger denominator, int bitLength, int reducedBits, in LogPlan plan,
        out long seriesExponent)
    {
        var workingBits = plan.WorkingBits;

        // Y = floor(U · 2^sh / D) in [2^(W-1), 2^(W+1)), |y| = Y · 2^-sh with a relative error below 2^-(W-1).
        var shift = workingBits + bitLength - reducedBits + 1;
        var y = (reduced << shift) / denominator;

        // Z = floor(Y² · 2^(W - 2sh)) = z in W fraction bits.
        var z = (y * y) >> (2 * shift - workingBits);
        var t = plan.BlockSize == 0
            ? AtanhSeriesHorner(z, plan.Terms, workingBits)
            : AtanhSeriesPatersonStockmeyer(z, plan.Terms, plan.BlockSize, workingBits);
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

    // Σ_{k<N} Z^k / (2k+1) in Paterson–Stockmeyer form (§10.1): powers Z_0 = 2^W .. Z_m; blocks of m terms from the
    // top, H = floor(H · Z_m / 2^W) + Σ_{i<m} floor(Z_i / (2(a + i) + 1)) for the block starting at term a.
    private static BigInteger AtanhSeriesPatersonStockmeyer(BigInteger z, int terms, int blockSize, int workingBits)
    {
        var powers = Powers(z, blockSize, workingBits);
        var top = (terms - 1) / blockSize * blockSize;
        BigInteger h = 0;
        for (var i = 0; i < terms - top; i++)
        {
            h += powers[i] / (2 * (top + i) + 1);
        }

        for (var first = top - blockSize; first >= 0; first -= blockSize)
        {
            h = h * powers[blockSize] >> workingBits;
            for (var i = 0; i < blockSize; i++)
            {
                h += powers[i] / (2 * (first + i) + 1);
            }
        }

        return h;
    }

    // X_0 = 2^W, X_1 = X, X_i = floor(X_(i-1) · X_1 / 2^W) for odd i and floor(X_(i/2)² / 2^W) for even i.
    private static BigInteger[] Powers(BigInteger x, int count, int workingBits)
    {
        var powers = new BigInteger[count + 1];
        powers[0] = BigInteger.One << workingBits;
        powers[1] = x;
        for (var i = 2; i <= count; i++)
        {
            powers[i] = (i % 2 == 0 ? powers[i / 2] * powers[i / 2] : powers[i - 1] * powers[1]) >> workingBits;
        }

        return powers;
    }

    // floor(|e| · L / 2^(Lbits - F)) with L = floor(ln 2 · 2^Lbits), Lbits rounded up to a word as on the GPU.
    private static BigInteger MultiplyLn2(long e, int ln2FractionBits, int fractionBits)
    {
        var ln2Bits = (ln2FractionBits + 31) / 32 * 32;
        var ln2 = GpuBigFloatConstants.GetLn2Fixed(ln2Bits);
        return Math.Abs(e) * ln2 >> (ln2Bits - fractionBits);
    }

    // floor((A + 2^(g-1)) / 2^g) = floor((floor(A / 2^(g-1)) + 1) / 2) with g = bits(A) - p, normalized to p bits.
    private static (BigInteger Value, long Exponent) RoundToNearest(
        in TranscendentalApproximation approximation, int resultBits)
    {
        var guardBits = (int)approximation.Significand.GetBitLength() - resultBits;
        var kept = (approximation.Significand >> (guardBits - 1)) + 1;
        return Normalize(kept, approximation.Exponent + guardBits - 1, resultBits);
    }

    // Shifts a positive value to exactly `bits` bits (truncating), adjusting the exponent.
    private static (BigInteger Value, long Exponent) Normalize(BigInteger value, long exponent, int bits)
    {
        var shift = bits - (int)value.GetBitLength();
        return shift >= 0 ? (value << shift, exponent - shift) : (value >> -shift, exponent - shift);
    }
}
