namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

/// <summary>
/// Computes and caches on the host the mathematical constants that the big-float transcendental functions share.
/// </summary>
/// <remarks>
/// <para>
/// <b>ln 2:</b> evaluated with the Machin-type formula <c>ln 2 = 18·atanh(1/26) − 2·atanh(1/4801) + 8·atanh(1/8749)</c>.
/// Each series <c>atanh(1/q) = Σ 1 / ((2k+1)·q^(2k+1))</c> is summed exactly by binary splitting over a number of terms
/// derived from the remainder bound, and truncated once. The combined value carries a known error interval; it is
/// rounded down to the requested fraction bits only when the whole interval rounds to the same integer, and
/// recomputed with more guard bits otherwise, so <see cref="GetLn2Fixed"/> returns exactly <c>floor(ln 2 · 2^F)</c>.
/// </para>
/// <para>
/// <b>Thread Safety:</b> the process-wide cache is guarded by a lock.
/// </para>
/// </remarks>
internal static class GpuBigFloatConstants
{
    // Guard bits added to the requested fraction bits before the series are truncated.
    private const int GuardBits = 8;

    private static readonly Lock s_ln2Lock = new();
    private static BigInteger s_ln2Fixed;
    private static int s_ln2FractionBits = -1;

    /// <summary>
    /// Returns <c>floor(ln 2 · 2^fractionBits)</c>, the natural logarithm of 2 as a fixed-point integer.
    /// </summary>
    /// <param name="fractionBits">The number of fraction bits; at least 0.</param>
    /// <returns>The value rounded toward zero to <paramref name="fractionBits"/> fraction bits (error below <c>2^-fractionBits</c>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fractionBits"/> is negative.</exception>
    /// <remarks>
    /// The value is cached for the process. A request beyond the cached precision recomputes it with at least twice
    /// the cached fraction bits, so a growing sequence of requests costs at most about twice the largest one. A smaller
    /// request is a right shift of the cached value: <c>floor(floor(v·2^a) / 2^b) = floor(v·2^(a−b))</c>.
    /// </remarks>
    internal static BigInteger GetLn2Fixed(int fractionBits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fractionBits);
        lock (s_ln2Lock)
        {
            if (fractionBits > s_ln2FractionBits)
            {
                var bits = (int)Math.Min(int.MaxValue - 64L, Math.Max(fractionBits, 2L * s_ln2FractionBits));
                s_ln2Fixed = ComputeLn2Fixed(bits);
                s_ln2FractionBits = bits;
            }

            return s_ln2Fixed >> (s_ln2FractionBits - fractionBits);
        }
    }

    /// <summary>
    /// Returns the number of terms <c>N</c> of <c>atanh(1/q) = Σ_{k≥0} 1 / ((2k+1)·q^(2k+1))</c> after which the
    /// remainder is at most <c>2^-fractionBits</c>.
    /// </summary>
    /// <param name="q">The reciprocal argument; at least 2.</param>
    /// <param name="fractionBits">The number of fraction bits the remainder must stay below.</param>
    /// <returns>
    /// The smallest <c>N ≥ 1</c> with <c>(q² − 1)·q^(2N−1) ≥ 2^fractionBits</c>. The remainder is below
    /// <c>Σ_{k≥N} q^-(2k+1) = 1 / ((q² − 1)·q^(2N−1))</c>.
    /// </returns>
    internal static int AtanhTermCount(int q, int fractionBits)
    {
        var estimate = (fractionBits - Math.Log2((double)q * q - 1)) / (2 * Math.Log2(q)) + 0.5;
        var terms = Math.Max(1, (int)Math.Ceiling(estimate));
        while (terms > 1 && CoversRemainder(q, terms - 1, fractionBits))
        {
            terms--;
        }

        while (!CoversRemainder(q, terms, fractionBits))
        {
            terms++;
        }

        return terms;
    }

    /// <summary>
    /// Returns <c>floor(S_N · 2^fractionBits)</c> for the partial sum <c>S_N = Σ_{k=0}^{N−1} 1 / ((2k+1)·q^(2k+1))</c>
    /// of <c>atanh(1/q)</c>.
    /// </summary>
    /// <param name="q">The reciprocal argument; at least 2.</param>
    /// <param name="terms">The number of terms <c>N</c>; at least 1.</param>
    /// <param name="fractionBits">The number of fraction bits of the result.</param>
    /// <returns>The partial sum rounded down to <paramref name="fractionBits"/> fraction bits.</returns>
    /// <remarks>The partial sum is an exact rational (binary splitting), so the only rounding is the final floor.</remarks>
    internal static BigInteger AtanhReciprocalFixed(int q, int terms, int fractionBits)
    {
        BigInteger qSquared = q * (long)q;
        var (t, b, power) = SplitAtanh(qSquared, 0, terms);

        // S_N = T / (q · B · Q)
        return (t << fractionBits) / (q * b * power);
    }

    // Sum_{k=a}^{b-1} q^(-2(k-a)) / (2k+1) = T / (B · Q) with B = prod (2k+1) and Q = q^(2(b-a)).
    // Leaf [a, a+1): T = Q = q^2, B = 2a + 1. Merge of [a, m) and [m, b):
    // T / (B Q) = T_l / (B_l Q_l) + T_r / (Q_l B_r Q_r) = (T_l B_r Q_r + B_l T_r) / (B_l B_r Q_l Q_r).
    private static (BigInteger T, BigInteger B, BigInteger Q) SplitAtanh(BigInteger qSquared, long first, long end)
    {
        if (end - first == 1)
        {
            return (qSquared, 2 * first + 1, qSquared);
        }

        var middle = first + (end - first) / 2;
        var (leftT, leftB, leftQ) = SplitAtanh(qSquared, first, middle);
        var (rightT, rightB, rightQ) = SplitAtanh(qSquared, middle, end);
        return (leftT * rightB * rightQ + leftB * rightT, leftB * rightB, leftQ * rightQ);
    }

    private static bool CoversRemainder(int q, int terms, int fractionBits) =>
        (q * (long)q - 1) * BigInteger.Pow(q, 2 * terms - 1) >= BigInteger.One << fractionBits;

    /// <summary>
    /// Computes <c>floor(ln 2 · 2^fractionBits)</c> without the cache.
    /// </summary>
    /// <param name="fractionBits">The number of fraction bits; at least 0.</param>
    /// <returns>The value rounded toward zero to <paramref name="fractionBits"/> fraction bits.</returns>
    internal static BigInteger ComputeLn2Fixed(int fractionBits)
    {
        for (var guardedBits = fractionBits + GuardBits; ; guardedBits += 32)
        {
            var sum = 18 * AtanhFixed(26, guardedBits)
                      - 2 * AtanhFixed(4801, guardedBits)
                      + 8 * AtanhFixed(8749, guardedBits);

            // Each truncated series lies below atanh(1/q)·2^G by d in (0, 2): the floor loses less than 1 and the
            // remainder is at most 1. With the weights 18, -2 and 8, ln 2·2^G lies in (sum - 4, sum + 52).
            var shift = guardedBits - fractionBits;
            var low = (sum - 4) >> shift;
            var high = (sum + 52) >> shift;
            if (low == high)
            {
                return low;
            }
        }
    }

    private static BigInteger AtanhFixed(int q, int fractionBits) =>
        AtanhReciprocalFixed(q, AtanhTermCount(q, fractionBits), fractionBits);
}
