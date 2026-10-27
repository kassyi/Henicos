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

    /// <summary>Returns <c>ceil(log2 value)</c>, 0 for <c>value ≤ 1</c>.</summary>
    internal static int CeilingLog2(ulong value) => value <= 1 ? 0 : 64 - BitOperations.LeadingZeroCount(value - 1);

    private static bool CoversRemainder(int terms, int workingBits, BigInteger zNum, BigInteger zDen) =>
        BigInteger.Pow(zNum, terms) << workingBits
        <= (2 * terms + 1) * (zDen - zNum) * BigInteger.Pow(zDen, terms - 1);
}
