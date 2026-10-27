namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// The parameters of one big-float <c>Log</c> evaluation, chosen by <see cref="TranscendentalPlanner.PlanLog"/>.
/// </summary>
/// <param name="ResultBits">The result precision <c>p</c> in bits (32 per result word).</param>
/// <param name="GuardWords">The guard words <c>gw</c> of the working precision <c>W = p + 32·gw</c>.</param>
/// <param name="Terms">
/// The number of terms <c>N</c> of <c>T = Σ_{k&lt;N} z^k / (2k+1)</c>, <c>z = y²</c>; 0 when the reduced argument
/// <c>m</c> is exactly 1 and no series is needed.
/// </param>
/// <param name="ErrorBound">
/// The bound <c>B</c> of the <c>W</c>-bit approximation <c>A</c>: <c>|A − ln x| ≤ B</c> units of its last bit.
/// </param>
/// <param name="Ln2FractionBits">
/// The fraction bits of ln 2 for <c>e·ln 2</c>, <c>W + ceil(log2 |e|) + 4</c>, or 0 when <c>e = 0</c>.
/// </param>
internal readonly record struct LogPlan(int ResultBits, int GuardWords, int Terms, int ErrorBound, int Ln2FractionBits)
{
    /// <summary>Gets the working precision <c>W = p + 32·gw</c> in bits, a multiple of 32.</summary>
    public int WorkingBits => ResultBits + 32 * GuardWords;
}

/// <summary>
/// The parameters of one big-float <c>Exp</c> evaluation, chosen by <see cref="TranscendentalPlanner.PlanExp"/>.
/// </summary>
/// <param name="ResultBits">The result precision <c>p</c> in bits (32 per result word).</param>
/// <param name="GuardWords">The guard words <c>gw</c> of the working precision <c>W = p + 32·gw</c>.</param>
/// <param name="Reduction">The multiple <c>k</c> of ln 2 subtracted: <c>x = k·ln 2 + r</c>, <c>r ∈ (0.69, 1.39)</c>.</param>
/// <param name="Halvings">The halvings <c>s ≥ 2</c>: the series runs at <c>r / 2^s</c> and is squared <c>s</c> times.</param>
/// <param name="Terms">The degree <c>N</c> of the Taylor polynomial <c>Σ_{j≤N} ρ^j / j!</c>.</param>
/// <param name="ErrorBound">
/// The bound <c>B</c> of the <c>W</c>-bit approximation <c>A</c> of <c>e^r</c>: <c>|A − e^r| ≤ B</c> units of its last bit.
/// </param>
/// <param name="Ln2FractionBits">The fraction bits of ln 2 for <c>k·ln 2</c>, <c>W − s + bits(|k|) + 2</c>.</param>
internal readonly record struct ExpPlan(
    int ResultBits, int GuardWords, long Reduction, int Halvings, int Terms, BigInteger ErrorBound, int Ln2FractionBits)
{
    /// <summary>Gets the working precision <c>W = p + 32·gw</c> in bits, a multiple of 32.</summary>
    public int WorkingBits => ResultBits + 32 * GuardWords;
}

/// <summary>
/// Chooses on the host, before any device work, the term counts and working precisions of the big-float
/// transcendental functions, so that the series run a fixed number of steps without reading values back.
/// </summary>
/// <remarks>
/// <para>
/// The GPU (<see cref="GpuBigFloatTranscendentals"/>) and the CPU (<see cref="BigFloatTranscendentalMath"/>)
/// implementations take the same plan, which keeps them bit-identical.
/// </para>
/// <para>
/// The error bounds follow the design of #628 (atanh series from the top in Horner form:
/// <c>F &lt; 1.79 + ε'_R</c> units; Log approximation <c>B = ceil(F + 4.01)</c>).
/// </para>
/// </remarks>
internal static class TranscendentalPlanner
{
    /// <summary>
    /// The bound <c>B</c> of the Log approximation with the atanh series in Horner form:
    /// <c>ceil(1.79 + ε'_R + 4.01)</c> with <c>ε'_R ≤ 1</c>.
    /// </summary>
    internal const int LogHornerErrorBound = 7;

    // Margin of the guard bits above the error bound; the rounding check fails with probability about 2^-15.
    private const int GuardMarginBits = 16;

    // The largest s that the planner tries on its own (an explicit ExpHalvingCount may exceed it).
    private const int MaxPlannedHalvings = 64;

    // ln 2 rounded to a double (relative error below 2^-53).
    private const double Ln2 = 0.6931471805599453;

    // 2^32: every Exp argument at or above it in magnitude overflows (or underflows) the exponent of the result.
    private const double ArgumentLimit = 4294967296.0;

    /// <summary>
    /// Plans a Log of <c>x = m·2^e</c>, <c>m ∈ [3/4, 3/2)</c>.
    /// </summary>
    /// <param name="resultBits">The result precision <c>p</c>; at least 1.</param>
    /// <param name="e">The power of two extracted from the argument.</param>
    /// <param name="closenessBits">
    /// The closeness <c>c ≥ 1</c> of <c>m</c> to 1, <c>|m − 1| &lt; 2^-c</c>, or <see cref="int.MaxValue"/> when
    /// <c>m = 1</c>.
    /// </param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resultBits"/> or <paramref name="closenessBits"/>
    /// is below 1.</exception>
    internal static LogPlan PlanLog(int resultBits, long e, int closenessBits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resultBits, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(closenessBits, 1);

        var guardWords = GuardWordsFor(LogHornerErrorBound);
        var workingBits = checked(resultBits + 32 * guardWords);
        var terms = 0;
        if (closenessBits != int.MaxValue)
        {
            // A smaller c is a weaker but still valid bound; from c = W / 2 on, one term already suffices.
            var (zNum, zDen) = AtanhSquareBound(Math.Min(closenessBits, workingBits));
            terms = AtanhTermCount(workingBits, zNum, zDen);
        }

        var ln2FractionBits = e == 0 ? 0 : checked(workingBits + CeilingLog2((ulong)Math.Abs(e)) + 4);
        return new LogPlan(resultBits, guardWords, terms, LogHornerErrorBound, ln2FractionBits);
    }

    /// <summary>
    /// Returns the guard words <c>ceil((ceil(log2(B + 1)) + 16) / 32)</c> for an error bound <c>B</c>.
    /// </summary>
    internal static int GuardWordsFor(int errorBound) =>
        (CeilingLog2((ulong)errorBound + 1) + GuardMarginBits + 31) / 32;

    /// <summary>
    /// Returns an upper bound <c>Num / Den</c> of <c>z = y²</c>, <c>y = (m − 1) / (m + 1)</c>, for
    /// <c>|m − 1| &lt; 2^-c</c>.
    /// </summary>
    /// <remarks>
    /// <c>|y| ≤ 1/5</c> on <c>m ∈ [3/4, 3/2)</c>, so <c>z ≤ 1/25</c>. Near 1, <c>|y| ≤ |m − 1| / 1.75</c> gives
    /// <c>z &lt; 2^(-2c) / 3.06 &lt; 1 / (3·4^c)</c>, which is the smaller bound from <c>c = 2</c> on.
    /// </remarks>
    internal static (BigInteger Num, BigInteger Den) AtanhSquareBound(int closenessBits) =>
        closenessBits >= 2
            ? (BigInteger.One, 3 * (BigInteger.One << (2 * closenessBits)))
            : (BigInteger.One, new BigInteger(25));

    /// <summary>
    /// Returns the smallest <c>N ≥ 1</c> whose remainder <c>ε'_R = z^N / ((2N+1)(1 − z)·2^-W)</c> is at most 1 for
    /// <c>z ≤ zNum / zDen</c>: <c>zNum^N · 2^W ≤ (2N+1)(zDen − zNum)·zDen^(N−1)</c>.
    /// </summary>
    internal static int AtanhTermCount(int workingBits, BigInteger zNum, BigInteger zDen)
    {
        var bitsPerTerm = BigInteger.Log(zDen, 2) - BigInteger.Log(zNum, 2);
        var terms = Math.Max(1, (int)Math.Ceiling(workingBits / bitsPerTerm));
        while (terms > 1 && CoversRemainder(terms - 1, workingBits, zNum, zDen))
        {
            terms--;
        }

        while (!CoversRemainder(terms, workingBits, zNum, zDen))
        {
            terms++;
        }

        return terms;
    }

    /// <summary>
    /// Plans an Exp of <c>x</c>, <c>|x| &lt; 2^32</c>, from a double approximation of <c>x</c>.
    /// </summary>
    /// <param name="resultBits">The result precision <c>p</c>; at least 1.</param>
    /// <param name="xApprox">
    /// <c>x</c> to a relative error of about <c>2^-52</c> (its top 64 bits rounded to a double); <c>|xApprox| &lt; 2^32</c>.
    /// </param>
    /// <param name="options">The tuning parameters (<see cref="GpuBigFloatTranscendentalOptions.ExpHalvingCount"/>).</param>
    /// <returns>The plan.</returns>
    /// <exception cref="OverflowException">The exponent of every possible result lies outside <see cref="int"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>k = floor(x / ln 2) − 1</c>: the double quotient is within <c>2^-19</c> of <c>x / ln 2</c> for
    /// <c>|x| &lt; 2^32</c>, so <c>r = x − k·ln 2 ∈ ((1 − 2^-19)·ln 2, (2 + 2^-19)·ln 2) ⊂ (0.69, 1.39)</c> and needs no
    /// sign test on the device.
    /// </para>
    /// <para>
    /// The series is the term recurrence <c>T_j = floor(floor(T_(j−1)·R / 2^W) / j)</c> (stage 3 of #628), whose bound is
    /// <c>E &lt; 2^s·(N + 0.6·ln(N+1) + ε_R + 1.7) − 1</c> with <c>ε_R = 2ρ^(N+1) / ((N+1)!·2^-W) ≤ 1</c>,
    /// <c>ρ ≤ 1.39·2^-s ≤ 1/2</c>. Without a fixed <c>s</c>, <c>s ∈ [2, 64]</c> minimizes <c>(N + s)·W</c>, the number of
    /// full-width multiplications weighted by their width, the smaller <c>s</c> on a tie.
    /// </para>
    /// </remarks>
    internal static ExpPlan PlanExp(int resultBits, double xApprox, GpuBigFloatTranscendentalOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resultBits, 1);
        ArgumentNullException.ThrowIfNull(options);

        // |x| >= 2^32 is out of range whatever p is; this also keeps k in range of long below.
        if (!(Math.Abs(xApprox) < ArgumentLimit))
        {
            throw ExpOutOfRange(negative: xApprox < 0);
        }

        var reduction = (long)Math.Floor(xApprox / Ln2) - 1;

        // e^x lies in (2^(k+1), 2^(k+2)) up to 2^-18, so its p-bit exponent lies in [k + 1 - p, k + 3 - p], one more
        // after a rounding carry. Throw only when every value of that range is outside int; the exact exponent is
        // checked again when the result is assembled.
        if (reduction + 1L - resultBits > int.MaxValue)
        {
            throw ExpOutOfRange(negative: false);
        }

        if (reduction + 4L - resultBits < int.MinValue)
        {
            throw ExpOutOfRange(negative: true);
        }

        var first = options.ExpHalvingCount ?? GpuBigFloatTranscendentalOptions.MinExpHalvingCount;
        var last = options.ExpHalvingCount ?? MaxPlannedHalvings;
        var best = default(ExpPlan);
        var bestCost = long.MaxValue;
        for (var halvings = first; halvings <= last; halvings++)
        {
            // The guard words depend on the bound, which depends on N, which depends on W: grow gw until it holds.
            var guardWords = 1;
            int terms;
            BigInteger errorBound;
            while (true)
            {
                terms = ExpTermCount(checked(resultBits + 32 * guardWords), halvings);
                errorBound = ExpRecurrenceErrorBound(halvings, terms);
                var needed = GuardWordsFor(errorBound);
                if (needed <= guardWords)
                {
                    break;
                }

                guardWords = needed;
            }

            var workingBits = resultBits + 32 * guardWords;
            var cost = (long)(terms + halvings) * workingBits;
            if (cost < bestCost)
            {
                bestCost = cost;
                var ln2FractionBits = checked(workingBits - halvings + BitLength(reduction) + 2);
                best = new ExpPlan(resultBits, guardWords, reduction, halvings, terms, errorBound, ln2FractionBits);
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the bound of the Exp term recurrence, <c>ceil(2^s·(N + 0.6·ln(N+1) + 2.7)) − 1</c> (<c>ε_R ≤ 1</c>).
    /// </summary>
    internal static BigInteger ExpRecurrenceErrorBound(int halvings, int terms)
    {
        // ceil(X · 2^30) / 2^30 >= X, scaled exactly by 2^s.
        var factor = terms + 0.6 * Math.Log(terms + 1) + 2.7;
        var scaled = new BigInteger(Math.Ceiling(factor * (1 << 30)));
        return halvings >= 30
            ? (scaled << (halvings - 30)) - 1
            : ((scaled + (BigInteger.One << (30 - halvings)) - 1) >> (30 - halvings)) - 1;
    }

    /// <summary>
    /// Returns the smallest <c>N ≥ 1</c> with <c>ε_R = 2ρ^(N+1) / ((N+1)!·2^-W) ≤ 1</c> for <c>ρ = 1.39·2^-s</c>:
    /// <c>2·139^(N+1)·2^W ≤ (N+1)!·(100·2^s)^(N+1)</c>.
    /// </summary>
    internal static int ExpTermCount(int workingBits, int halvings)
    {
        // log2 of the right side minus the left side grows with N; start from the first N where the double estimate
        // turns non-negative, then settle it exactly.
        var bitsPerTerm = Math.Log2(100) + halvings - Math.Log2(139);
        var logFactorial = 1.0; // log2((N+1)!) for N = 1
        var terms = 1;
        while (logFactorial + (terms + 1) * bitsPerTerm - 1 - workingBits < 0)
        {
            terms++;
            logFactorial += Math.Log2(terms + 1);
        }

        while (terms > 1 && ExpRemainderCovered(terms - 1, workingBits, halvings))
        {
            terms--;
        }

        while (!ExpRemainderCovered(terms, workingBits, halvings))
        {
            terms++;
        }

        return terms;
    }

    /// <summary>Returns the guard words <c>ceil((bits(B) + 16) / 32)</c> for an error bound <c>B</c>.</summary>
    internal static int GuardWordsFor(BigInteger errorBound) =>
        checked((int)errorBound.GetBitLength() + GuardMarginBits + 31) / 32;

    /// <summary>
    /// Returns the result exponent as <see cref="int"/>, or throws the Exp overflow (or underflow) exception.
    /// </summary>
    internal static int ToExpResultExponent(long exponent) =>
        exponent > int.MaxValue ? throw ExpOutOfRange(negative: false)
        : exponent < int.MinValue ? throw ExpOutOfRange(negative: true)
        : (int)exponent;

    /// <summary>
    /// Creates the exception for an Exp result whose exponent does not fit in <see cref="int"/>: an overflow for a
    /// positive argument, an underflow (below <c>2^int.MinValue</c>) for a negative one.
    /// </summary>
    internal static OverflowException ExpOutOfRange(bool negative) =>
        negative
            ? new OverflowException("Exp underflow: the result is below the smallest exponent of a big float (2^int.MinValue).")
            : new OverflowException("Exp overflow: the result exceeds the largest exponent of a big float (2^int.MaxValue).");

    /// <summary>Returns <c>ceil(log2 value)</c>, 0 for <c>value ≤ 1</c>.</summary>
    internal static int CeilingLog2(ulong value) => value <= 1 ? 0 : 64 - BitOperations.LeadingZeroCount(value - 1);

    // Bit length of |value| (0 for 0).
    private static int BitLength(long value) =>
        value == 0 ? 0 : 64 - BitOperations.LeadingZeroCount((ulong)Math.Abs(value));

    // 2 · 139^(N+1) · 2^W <= (N+1)! · 100^(N+1) · 2^(s(N+1))
    private static bool ExpRemainderCovered(int terms, int workingBits, int halvings)
    {
        BigInteger factorial = 1;
        for (var i = 2; i <= terms + 1; i++)
        {
            factorial *= i;
        }

        var left = 2 * BigInteger.Pow(139, terms + 1);
        var right = factorial * BigInteger.Pow(100, terms + 1);
        var shift = workingBits - (long)halvings * (terms + 1);
        return shift >= 0 ? left << (int)shift <= right : left <= right << (int)-shift;
    }

    private static bool CoversRemainder(int terms, int workingBits, BigInteger zNum, BigInteger zDen) =>
        BigInteger.Pow(zNum, terms) << workingBits
        <= (2 * terms + 1) * (zDen - zNum) * BigInteger.Pow(zDen, terms - 1);
}
