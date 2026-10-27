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
    /// <summary>The message of the exception for <c>Pow(0, y)</c> (the CPU and the GPU provider).</summary>
    internal const string PowOfZeroMessage = "Pow with zero base is not supported.";

    /// <summary>The message of the exception for a Pow approximation with the base 1 (a precondition violation).</summary>
    internal const string PowApproximationOfOneMessage =
        "The Pow approximation needs a base other than 1; Pow returns 1^y = 1 before any approximation.";

    /// <summary>
    /// Computes <c>ln x</c> for <c>x = significand · 2^exponent &gt; 0</c> to <paramref name="resultBits"/> bits.
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>
    /// The significand (exactly <paramref name="resultBits"/> bits, or zero for <c>ln 1</c>), exponent and sign of
    /// <c>RZ_p(ln x)</c>, the exact value rounded toward zero to <c>p</c> bits.
    /// </returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static (BigInteger Significand, int Exponent, bool IsNegative) Log(
        BigInteger significand, int exponent, int resultBits, GpuBigFloatTranscendentalOptions options) =>
        Log(significand, exponent, resultBits, options, out _);

    /// <summary>
    /// Computes <c>RZ_p(ln x)</c> as <see cref="Log(BigInteger, int, int, GpuBigFloatTranscendentalOptions)"/> does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (1 unless the rounding
    /// check had to retry).</param>
    /// <returns>The significand, exponent and sign of the result.</returns>
    internal static (BigInteger Significand, int Exponent, bool IsNegative) Log(
        BigInteger significand, int exponent, int resultBits, GpuBigFloatTranscendentalOptions options, out int attempts)
    {
        attempts = 0;
        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            var approximation = LogApproximation(significand, exponent, resultBits, options, extraGuardWords);
            if (approximation.Significand.IsZero)
            {
                return (BigInteger.Zero, 0, false);
            }

            if (TryTruncate(approximation, resultBits, out var truncated, out var truncatedExponent))
            {
                return (truncated, checked((int)truncatedExponent), approximation.IsNegative);
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the <c>W</c>-bit approximation of <c>ln x</c>, <c>x = significand · 2^exponent &gt; 0</c>, and its bound.
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="extraGuardWords">Guard words beyond those the bound needs (a retry of the rounding check).</param>
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
        BigInteger significand, int exponent, int resultBits, GpuBigFloatTranscendentalOptions options,
        int extraGuardWords = 0)
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
        var plan = TranscendentalPlanner.PlanLog(resultBits, e, closeness, options, extraGuardWords);
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
    /// The significand (exactly <paramref name="resultBits"/> bits) and exponent of <c>RZ_p(e^x)</c>, the exact value
    /// rounded toward zero to <c>p</c> bits.
    /// </returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/> (an underflow,
    /// named in the message, for a negative argument).</exception>
    /// <remarks>
    /// <c>|x| ≥ 2^32</c> throws before any work. For <c>|x| &lt; 2^-(p+1)</c> the result is <c>RZ(e^x)</c> directly: 1, or
    /// <c>1 − 2^-p</c> for a negative <c>x</c>. Every other argument goes through <see cref="ExpApproximation"/> and the
    /// rounding check.
    /// </remarks>
    internal static (BigInteger Significand, int Exponent) Exp(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options) =>
        Exp(significand, exponent, negative, resultBits, options, out _);

    /// <summary>
    /// Computes <c>RZ_p(e^x)</c> as <see cref="Exp(BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/> does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="significand">The significand of the argument; zero or positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="negative">Whether the argument is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (0 for the shortcuts, 1
    /// unless the rounding check had to retry).</param>
    /// <returns>The significand and exponent of the result.</returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    internal static (BigInteger Significand, int Exponent) Exp(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options,
        out int attempts)
    {
        attempts = 0;
        if (significand.IsZero)
        {
            return (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        // |x| < 2^top. |x| >= 2^32 is out of range for every p (PlanExp rejects it as well); checking it here keeps an
        // argument with a huge exponent from being read as a double (Math.ScaleB to infinity) and planned at all.
        var top = (long)exponent + (long)significand.GetBitLength();
        if (top > 32)
        {
            throw TranscendentalPlanner.ExpOutOfRange(negative);
        }

        // |x| < 2^-(p+1): e^x < 1 + 2^-p for x > 0 and e^x > 1 - 2^-(p+1) > 1 - 2^-p for x < 0, so RZ(e^x) is 1 or
        // 1 - 2^-p without any series.
        if (top <= -(resultBits + 1))
        {
            return negative ? ((BigInteger.One << resultBits) - 1, -resultBits) : (BigInteger.One << (resultBits - 1), 1 - resultBits);
        }

        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            var approximation = ExpApproximation(significand, exponent, negative, resultBits, options, extraGuardWords);
            if (TryTruncate(approximation, resultBits, out var truncated, out var truncatedExponent))
            {
                return (truncated, TranscendentalPlanner.ToExpResultExponent(truncatedExponent));
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the approximation of <c>e^x</c>, <c>x = ±significand · 2^exponent</c>, and its bound, for an argument
    /// outside the shortcuts of <see cref="Exp(BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/> (<c>2^-(p+1) ≤ |x| &lt; 2^32</c>).
    /// </summary>
    /// <param name="significand">The significand of the argument; positive.</param>
    /// <param name="exponent">The exponent of the argument.</param>
    /// <param name="negative">Whether the argument is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="extraGuardWords">Guard words beyond those the bound needs (a retry of the rounding check).</param>
    /// <returns>The approximation <c>Q·2^(k−W)</c>, normalized exactly to <c>W + 32</c> bits, with <see cref="ExpPlan.ErrorBound"/>.</returns>
    /// <exception cref="OverflowException">The planner finds the result exponent out of range.</exception>
    /// <remarks>
    /// <c>x = k·ln 2 + r</c> with <c>k = floor(x / ln 2) − 1</c> from a double approximation, so <c>r ∈ (0.69, 1.39)</c>
    /// for either sign of <c>x</c>. <c>r</c> is formed with ln 2 at <c>Lw</c> fraction bits (<c>W − s + bits(|k|) + 2</c>
    /// rounded up to a word) and truncated once to <c>W − s</c> fraction bits, which is <c>R = r / 2^s</c> with <c>W</c>
    /// fraction bits. <c>|R·2^-W − r/2^s| &lt; 1.5·2^-W</c> (design §4.4.1), on either side: truncating <c>x</c> lowers
    /// <c>R</c> for <c>x &gt; 0</c> and raises it for <c>x &lt; 0</c>, truncating <c>L</c> does the opposite, so
    /// <c>R·2^-W</c> may exceed <c>r/2^s</c>. The bound of §10.1 uses the size of that error, and <c>ρ ≤ 1.39·2^-s</c>
    /// still holds with the excess (<c>r &lt; 1.3863·(1 + 2^-19)</c>). The Taylor polynomial of degree <c>N</c> runs in
    /// Paterson–Stockmeyer form with Smith's division order, the sum is squared <c>s</c> times, and
    /// <c>e^x = e^r · 2^k</c>.
    /// </remarks>
    internal static TranscendentalApproximation ExpApproximation(
        BigInteger significand, int exponent, bool negative, int resultBits, GpuBigFloatTranscendentalOptions options,
        int extraGuardWords = 0)
    {
        var top = (long)exponent + (long)significand.GetBitLength();
        var plan = TranscendentalPlanner.PlanExp(
            resultBits, ApproximateArgument(LeadingBits(significand), top, negative), options, extraGuardWords);
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

    /// <summary>
    /// Returns the top 64 bits of a positive value, its top bit at bit 63 (truncated below, zero-filled for a shorter
    /// value), as the device <see cref="IGpuBasicMathProvider.ReadLeadingBits"/>
    /// reads them.
    /// </summary>
    internal static ulong LeadingBits(BigInteger value)
    {
        var bitLength = (int)value.GetBitLength();
        return bitLength >= 64 ? (ulong)(value >> (bitLength - 64)) : (ulong)(value << (64 - bitLength));
    }

    /// <summary>
    /// Computes <c>x^y</c> for <c>x = xSignificand · 2^xExponent &gt; 0</c> and <c>y = ±ySignificand · 2^yExponent</c>
    /// to <paramref name="resultBits"/> bits.
    /// </summary>
    /// <param name="xSignificand">The significand of the base; positive.</param>
    /// <param name="xExponent">The exponent of the base.</param>
    /// <param name="ySignificand">The significand of the exponent; zero or positive.</param>
    /// <param name="yExponent">The exponent of the exponent.</param>
    /// <param name="yNegative">Whether the exponent is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <returns>
    /// The significand (exactly <paramref name="resultBits"/> bits) and exponent of <c>RZ_p(x^y)</c>, the exact value
    /// rounded toward zero to <c>p</c> bits.
    /// </returns>
    /// <exception cref="InvalidOperationException">The base is zero.</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/> (an underflow,
    /// named in the message, for a result below 1).</exception>
    /// <remarks>
    /// A power that is a <c>p</c>-bit value (<see cref="TryExactPow"/>: <c>x^0</c>, <c>1^y</c>, <c>2^3</c>, <c>4^0.5</c>,
    /// ...) is returned as it is. Every other power goes through <see cref="PowApproximation"/> and the rounding check,
    /// which retries with more guard words until it accepts; it ends because such a power lies strictly inside its
    /// <c>p</c>-bit cell (see <see cref="TryExactPow"/>).
    /// </remarks>
    internal static (BigInteger Significand, int Exponent) Pow(
        BigInteger xSignificand, int xExponent, BigInteger ySignificand, int yExponent, bool yNegative, int resultBits,
        GpuBigFloatTranscendentalOptions options) =>
        Pow(xSignificand, xExponent, ySignificand, yExponent, yNegative, resultBits, options, out _);

    /// <summary>
    /// Computes <c>RZ_p(x^y)</c> as <see cref="Pow(BigInteger, int, BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/>
    /// does, and reports the number of attempts of the rounding check.
    /// </summary>
    /// <param name="xSignificand">The significand of the base; positive.</param>
    /// <param name="xExponent">The exponent of the base.</param>
    /// <param name="ySignificand">The significand of the exponent; zero or positive.</param>
    /// <param name="yExponent">The exponent of the exponent.</param>
    /// <param name="yNegative">Whether the exponent is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="attempts">When this method returns, the number of approximations computed (0 for an exact power,
    /// 1 unless the rounding check had to retry).</param>
    /// <returns>The significand and exponent of the result.</returns>
    internal static (BigInteger Significand, int Exponent) Pow(
        BigInteger xSignificand, int xExponent, BigInteger ySignificand, int yExponent, bool yNegative, int resultBits,
        GpuBigFloatTranscendentalOptions options, out int attempts)
    {
        attempts = 0;
        if (xSignificand.IsZero)
        {
            throw new InvalidOperationException(PowOfZeroMessage);
        }

        if (TryExactPow(xSignificand, xExponent, ySignificand, yExponent, yNegative, resultBits, out var exact, out var exactExponent))
        {
            return (exact, exactExponent);
        }

        var extraGuardWords = 0;
        while (true)
        {
            attempts++;
            var approximation = PowApproximation(
                xSignificand, xExponent, ySignificand, yExponent, yNegative, resultBits, options, extraGuardWords);
            if (approximation.Significand.IsZero)
            {
                return approximation.IsNegative ? LargestBelowOne(resultBits) : One(resultBits);
            }

            if (TryTruncate(approximation, resultBits, out var truncated, out var truncatedExponent))
            {
                return (truncated, TranscendentalPlanner.ToResultExponent(truncatedExponent, "Pow"));
            }

            extraGuardWords += TranscendentalPlanner.ZivRetryGuardWords(approximation.WorkingBits);
        }
    }

    /// <summary>
    /// Computes the approximation of <c>x^y = e^(y·ln x)</c> and its bound, for a base other than 1 and a power that is
    /// not a <c>p</c>-bit value (after <see cref="TryExactPow"/>).
    /// </summary>
    /// <param name="xSignificand">The significand of the base; positive.</param>
    /// <param name="xExponent">The exponent of the base.</param>
    /// <param name="ySignificand">The significand of the exponent; positive.</param>
    /// <param name="yExponent">The exponent of the exponent.</param>
    /// <param name="yNegative">Whether the exponent is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>; a multiple of 32.</param>
    /// <param name="options">The tuning parameters of the workspace.</param>
    /// <param name="extraGuardWords">Guard words beyond those the bounds need (a retry of the rounding check).</param>
    /// <returns>
    /// The approximation of <c>e^(y·L)</c>, normalized exactly to <c>W + 32</c> bits, with the bound <c>B_E + D</c> of
    /// <c>x^y</c> itself; or, when <c>|y·L| &lt; 2^-(p+1)</c>, a zero significand with the sign of <c>y·ln x</c>
    /// (<c>RZ_p(x^y)</c> is then 1, or <c>1 − 2^-p</c> for a negative <c>y·ln x</c>).
    /// </returns>
    /// <exception cref="ArgumentException">The base is 1 (<see cref="Pow(BigInteger, int, BigInteger, int, bool, int, GpuBigFloatTranscendentalOptions)"/>
    /// returns <c>1^y = 1</c> before any approximation).</exception>
    /// <exception cref="OverflowException">The result exponent is out of range (<c>|y·L| ≥ 2^32</c>, or the planner's
    /// range of <see cref="TranscendentalPlanner.ExpReduction"/>); named Pow in the message.</exception>
    /// <remarks>
    /// <para>
    /// <c>L</c> is the Log approximation at <see cref="TranscendentalPlanner.PowLogResultBits"/> (the Exp precision plus
    /// 64 bits), <c>|L − ln x| ≤ B_L·2^U_L</c>. <c>t̃ = y·L</c> is formed exactly and has the sign of <c>t = y·ln x</c>
    /// (the sign of <c>L</c> is that of <c>ln x</c>). The Exp approximation of <c>e^t̃</c> carries its bound <c>B_E</c>;
    /// <see cref="TranscendentalPlanner.PowArgumentErrorBound"/> adds <c>D</c> for <c>|t − t̃|</c>.
    /// </para>
    /// <para>
    /// The shortcut: <c>|t − t̃| ≤ |t̃|·B_L·2^(1 − W_L) &lt; |t̃|·2^-50</c>, so <c>|t̃| &lt; 2^-(p+1)</c> gives
    /// <c>|t| &lt; 2^-p·(1 − 2^-(p+1))</c>: <c>e^t &lt; 1 + 2^-p</c> for <c>t &gt; 0</c>, <c>e^t &gt; 1 − 2^-p</c> for
    /// <c>t &lt; 0</c>, the same two results as the shortcut of Exp.
    /// </para>
    /// </remarks>
    internal static TranscendentalApproximation PowApproximation(
        BigInteger xSignificand, int xExponent, BigInteger ySignificand, int yExponent, bool yNegative, int resultBits,
        GpuBigFloatTranscendentalOptions options, int extraGuardWords = 0)
    {
        var expBits = TranscendentalPlanner.ExpWorkingBits(resultBits, options, extraGuardWords);
        var log = LogApproximation(xSignificand, xExponent, TranscendentalPlanner.PowLogResultBits(expBits), options);
        if (log.Significand.IsZero)
        {
            throw new ArgumentException(PowApproximationOfOneMessage, nameof(xSignificand));
        }

        var argument = ySignificand * log.Significand;
        var argumentExponent = yExponent + log.Exponent;
        var negative = yNegative != log.IsNegative;
        var top = argumentExponent + (long)argument.GetBitLength();
        if (top > 32)
        {
            throw TranscendentalPlanner.OutOfRange("Pow", negative);
        }

        if (top <= -(resultBits + 1))
        {
            return new TranscendentalApproximation(BigInteger.Zero, 0, negative, BigInteger.Zero, expBits);
        }

        // The planner's range check with the Pow message; ExpApproximation repeats it with the same argument.
        _ = TranscendentalPlanner.ExpReduction(resultBits, ApproximateArgument(LeadingBits(argument), top, negative), "Pow");
        var exp = ExpApproximation(argument, checked((int)argumentExponent), negative, resultBits, options, extraGuardWords);
        var argumentBound = TranscendentalPlanner.PowArgumentErrorBound(
            ySignificand, yExponent, log.ErrorBound, log.UnitExponent, exp.WorkingBits);
        return exp with { ErrorBound = exp.ErrorBound + argumentBound };
    }

    /// <summary>
    /// Returns whether <c>x^y</c> (<c>x = xSignificand · 2^xExponent &gt; 0</c>, <c>y = ±ySignificand · 2^yExponent</c>)
    /// is a <c>p</c>-bit value, and that value when it is: the exact check of Pow before any approximation.
    /// </summary>
    /// <param name="xSignificand">The significand of the base; positive.</param>
    /// <param name="xExponent">The exponent of the base.</param>
    /// <param name="ySignificand">The significand of the exponent; zero or positive.</param>
    /// <param name="yExponent">The exponent of the exponent.</param>
    /// <param name="yNegative">Whether the exponent is negative.</param>
    /// <param name="resultBits">The result precision <c>p</c>.</param>
    /// <param name="significand">When this method returns <see langword="true"/>, the significand of <c>x^y</c> with
    /// exactly <c>p</c> bits.</param>
    /// <param name="exponent">When this method returns <see langword="true"/>, its exponent.</param>
    /// <returns><see langword="true"/> when <c>x^y</c> is a <c>p</c>-bit value.</returns>
    /// <exception cref="OverflowException">The power is exact but its exponent does not fit in <see cref="int"/>
    /// (named Pow; an underflow below 1).</exception>
    /// <remarks>
    /// <para>
    /// Source: <c>mpfr_pow_is_exact</c> in <c>src/pow.c</c> of MPFR 4.2.2 (Copyright 2001-2025 Free Software Foundation,
    /// Inc.; file retrieved on 2026-10-27 from https://www.mpfr.org/mpfr-current/mpfr-4.2.2.tar.gz). It writes
    /// <c>x = a·2^b</c> and <c>y = c·2^d</c> with <c>a</c> and <c>c</c> odd and takes <c>−d</c> square roots of
    /// <c>a·2^b</c>, each with a perfect-square test. MPFR calls it for a non-integer <c>y</c> after the first failed
    /// rounding test, and handles an integer <c>y</c> by multiplication (<c>mpfr_pow_z</c>) and <c>x = 2^b</c> through
    /// <c>exp2</c>. Here one check covers the three cases before the first approximation (design #628 §4.8), so the
    /// rounding check never sees a <c>p</c>-bit value, which it could not accept (its guard bits are all zeros or all
    /// ones up to the error).
    /// </para>
    /// <para>
    /// With <c>x = a·2^b</c> and <c>y = ±c·2^d</c>, <c>a</c> and <c>c</c> odd:
    /// <list type="number">
    ///   <item><description><c>y</c> a non-negative integer: <c>x^y = a^y·2^(b·y)</c> is a <c>p</c>-bit value exactly when
    ///   the odd <c>a^y</c> has at most <c>p</c> bits.</description></item>
    ///   <item><description><c>y</c> a negative integer: <c>x^y = 2^(b·y) / a^|y|</c> is a dyadic only for
    ///   <c>a = 1</c>.</description></item>
    ///   <item><description><c>y = ±c / 2^k</c>, <c>k = −d ≥ 1</c>: <c>x^(1/2^k)</c> is rational only when <c>2^k</c>
    ///   divides <c>b</c> and <c>a</c> is a perfect <c>2^k</c>-th power (<c>k</c> square roots); then
    ///   <c>x^y = r^(±c)</c>, <c>r = a'·2^(b / 2^k)</c>, a <c>p</c>-bit value when <c>a' = 1</c> or, for <c>+c</c>,
    ///   when <c>a'^c</c> has at most <c>p</c> bits. For <c>k ≤ 62</c> only the length of <c>a</c> rejects early
    ///   (a perfect <c>2^k</c>-th power of an odd <c>r ≥ 3</c> has more than <c>2^k</c> bits); every other <c>a</c>
    ///   goes through the square roots. Otherwise <c>x^(1/2^k)</c> is irrational, and so is
    ///   <c>x^(c/2^k)</c>: <c>c</c> is odd, so <c>1/2^k = u·c/2^k + v</c> for some integers <c>u</c>, <c>v</c>, and
    ///   <c>x^(1/2^k) = (x^(c/2^k))^u·x^v</c>.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// A power found not to be a <c>p</c>-bit value is therefore a non-dyadic rational (case 2, or case 3 with
    /// <c>−c</c> and <c>a' &gt; 1</c>), a dyadic longer than <c>p</c> bits (<c>a^y</c> or <c>a'^c</c> of more than
    /// <c>p</c> bits, such as <c>S^y ≥ 2^p</c>), or irrational (case 3). None of them is a <c>p</c>-bit value, so each
    /// lies strictly inside its <c>p</c>-bit cell, at a positive distance from both ends. The rounding check rejects
    /// an approximation only when its bound reaches an end of the cell, and that bound shrinks against the cell as
    /// the guard words grow at each retry; so the check accepts once <c>W</c> is large enough. For a dyadic longer
    /// than <c>p</c> bits that is at the latest when <c>W</c> passes its length (plus the bound), whatever its bits
    /// after the top <c>p</c>.
    /// </para>
    /// </remarks>
    internal static bool TryExactPow(
        BigInteger xSignificand, int xExponent, BigInteger ySignificand, int yExponent, bool yNegative, int resultBits,
        out BigInteger significand, out int exponent)
    {
        var (xOdd, xTwos) = OddPart(xSignificand, xExponent);

        // x^0 = 1 for every x (also y = -0), and 1^y = 1 for every y.
        if (ySignificand.IsZero || (xOdd.IsOne && xTwos == 0))
        {
            (significand, exponent) = One(resultBits);
            return true;
        }

        var (yOdd, yTwos) = OddPart(ySignificand, yExponent);
        BigInteger odd;
        BigInteger twos;
        if (yTwos >= 0)
        {
            // Cases 1 and 2: y = ±yOdd · 2^yTwos is an integer.
            var yTop = (long)yOdd.GetBitLength() + yTwos;
            if (!xOdd.IsOne)
            {
                // a^|y| > 2^((bits(a) - 1)·|y|) for an odd a > 1, so y >= p already gives more than p bits.
                if (yNegative || yTop > 31 || (long)(xOdd.GetBitLength() - 1) * (long)(yOdd << (int)yTwos) >= resultBits)
                {
                    return NotExact(out significand, out exponent);
                }

                var y = (int)(yOdd << (int)yTwos);
                odd = BigInteger.Pow(xOdd, y);
                if (odd.GetBitLength() > resultBits)
                {
                    return NotExact(out significand, out exponent);
                }

                twos = (BigInteger)xTwos * y;
            }
            else
            {
                // x = 2^b with b != 0: |b·y| >= |y| >= 2^63 is out of range without forming y.
                if (yTop > 63)
                {
                    throw TranscendentalPlanner.OutOfRange("Pow", negative: (xTwos < 0) != yNegative);
                }

                odd = BigInteger.One;
                twos = (BigInteger)xTwos * (yOdd << (int)yTwos);
                if (yNegative)
                {
                    twos = -twos;
                }
            }
        }
        else
        {
            // Case 3: y = ±yOdd / 2^k. A 2^k-th root with k > 62 is never rational here: it needs 2^k | b with
            // |b| < 2^63, or an odd part of more than 2^k bits.
            var k = -yTwos;
            if (k > 62)
            {
                return NotExact(out significand, out exponent);
            }

            BigInteger rootOdd;
            long rootTwos;
            if (xOdd.IsOne)
            {
                if (xTwos % (1L << (int)k) != 0)
                {
                    return NotExact(out significand, out exponent);
                }

                rootOdd = BigInteger.One;
                rootTwos = xTwos >> (int)k;
            }
            else
            {
                // A perfect 2^k-th power of an odd r >= 3 has more than 2^k bits; with k <= 62 the length check
                // alone decides (an odd part of more than 2^k bits takes at most 62 square roots).
                if ((1L << (int)k) > (long)xOdd.GetBitLength())
                {
                    return NotExact(out significand, out exponent);
                }

                rootOdd = xOdd;
                rootTwos = xTwos;
                for (var i = 0; i < k; i++)
                {
                    // a·2^b is a square exactly when b is even and the odd a is a square.
                    if ((rootTwos & 1) != 0)
                    {
                        return NotExact(out significand, out exponent);
                    }

                    var root = rootOdd.Sqrt();
                    if (root * root != rootOdd)
                    {
                        return NotExact(out significand, out exponent);
                    }

                    rootOdd = root;
                    rootTwos >>= 1;
                }
            }

            // x^y = r^(±yOdd) with r = rootOdd · 2^rootTwos; r^(-yOdd) is not a dyadic for rootOdd > 1.
            if (rootOdd.IsOne)
            {
                odd = BigInteger.One;
            }
            else if (yNegative || yOdd.GetBitLength() > 31 || (long)(rootOdd.GetBitLength() - 1) * (long)yOdd >= resultBits)
            {
                return NotExact(out significand, out exponent);
            }
            else
            {
                odd = BigInteger.Pow(rootOdd, (int)yOdd);
                if (odd.GetBitLength() > resultBits)
                {
                    return NotExact(out significand, out exponent);
                }
            }

            twos = rootTwos * yOdd;
            if (yNegative)
            {
                twos = -twos;
            }
        }

        // odd · 2^twos with p bits: significand odd · 2^(p - bits(odd)), exponent twos - (p - bits(odd)).
        var shift = resultBits - (int)odd.GetBitLength();
        var resultExponent = twos - shift;
        if (resultExponent > int.MaxValue || resultExponent < int.MinValue)
        {
            throw TranscendentalPlanner.OutOfRange("Pow", negative: resultExponent < 0);
        }

        significand = odd << shift;
        exponent = (int)resultExponent;
        return true;
    }

    // value = odd · 2^twos for a positive value.
    private static (BigInteger Odd, long Twos) OddPart(BigInteger value, long valueExponent)
    {
        var zeros = (int)BigInteger.TrailingZeroCount(value);
        return (value >> zeros, valueExponent + zeros);
    }

    private static bool NotExact(out BigInteger significand, out int exponent)
    {
        significand = BigInteger.Zero;
        exponent = 0;
        return false;
    }

    // 1 = 2^(p-1) · 2^(1-p).
    private static (BigInteger Significand, int Exponent) One(int resultBits) =>
        (BigInteger.One << (resultBits - 1), 1 - resultBits);

    // 1 - 2^-p = (2^p - 1) · 2^-p.
    private static (BigInteger Significand, int Exponent) LargestBelowOne(int resultBits) =>
        ((BigInteger.One << resultBits) - 1, -resultBits);

    // Σ_{j<=N} R^j / j! in Paterson–Stockmeyer form with Smith's division order (§10.1): powers R_0 = 2^W .. R_m;
    // the top block [a, N] starts from R_(N-a); for i from its top down to 1: acc = floor(acc / (a + i)) + R_(i-1);
    // every lower block [a, a + m) starts from acc = floor(acc · R_m / 2^W) and runs i = m down to 1 the same way.
    private static BigInteger ExpSeriesPatersonStockmeyer(BigInteger r, int terms, int blockSize, int workingBits)
    {
        // R_m is needed only by a lower block, which exists only for N >= m.
        var powers = Powers(r, Math.Min(blockSize, terms), workingBits);
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
        // Z_m is needed only by a lower block, which exists only for N - 1 >= m.
        var powers = Powers(z, Math.Min(blockSize, terms - 1), workingBits);
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

    // X_0 = 2^W, X_1 = X, X_i = floor(X_(i-1) · X_1 / 2^W) for odd i and floor(X_(i/2)² / 2^W) for even i, i <= count.
    private static BigInteger[] Powers(BigInteger x, int count, int workingBits)
    {
        var powers = new BigInteger[count + 1];
        powers[0] = BigInteger.One << workingBits;
        if (count >= 1)
        {
            powers[1] = x;
        }

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

    // The rounding check of Ziv (design §4.1). A_W is the approximation at W bits, g = W - p and G = A_W mod 2^g, all in
    // units of the last bit of A_W. The exact value lies within B units of A. Exp drops the 32 low bits of A, so A lies
    // in [A_W, A_W + 1): the interval reaches up to A_W + 1 + B, almost one unit more above A_W, and the integer check
    // G + B < 2^g, that is G + B + 1 <= 2^g, covers that unit; B <= G covers the lower end (A >= A_W). When both hold,
    // every value in the interval truncates to A_W >> g, so that is RZ_p of the exact value. The bound B is the planner's (design §10.1, re-derived in the review of stages
    // 1–4): a B below the true error would make this check accept a wrong truncation silently.
    private static bool TryTruncate(
        in TranscendentalApproximation approximation, int resultBits, out BigInteger truncated, out long exponent)
    {
        var excess = (int)approximation.Significand.GetBitLength() - approximation.WorkingBits;
        var atWorkingBits = approximation.Significand >> excess;
        var guardBits = approximation.WorkingBits - resultBits;
        var limit = BigInteger.One << guardBits;
        var guard = atWorkingBits & (limit - 1);
        truncated = atWorkingBits >> guardBits;
        exponent = approximation.Exponent + excess + guardBits;
        return approximation.ErrorBound <= guard && guard + approximation.ErrorBound < limit;
    }

    // Shifts a positive value to exactly `bits` bits (truncating), adjusting the exponent.
    private static (BigInteger Value, long Exponent) Normalize(BigInteger value, long exponent, int bits)
    {
        var shift = bits - (int)value.GetBitLength();
        return shift >= 0 ? (value << shift, exponent - shift) : (value >> -shift, exponent - shift);
    }
}
