#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// A non-negative dyadic rational <c>Mantissa · 2^Exponent</c>, compared by value.
/// </summary>
internal readonly record struct Dyadic(BigInteger Mantissa, long Exponent) : IComparable<Dyadic>
{
    public static Dyadic One => new(BigInteger.One, 0);

    public static Dyadic operator *(Dyadic left, Dyadic right) =>
        new(left.Mantissa * right.Mantissa, left.Exponent + right.Exponent);

    public static bool operator <(Dyadic left, Dyadic right) => left.CompareTo(right) < 0;

    public static bool operator >(Dyadic left, Dyadic right) => left.CompareTo(right) > 0;

    public static bool operator <=(Dyadic left, Dyadic right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Dyadic left, Dyadic right) => left.CompareTo(right) >= 0;

    public int CompareTo(Dyadic other)
    {
        if (Mantissa.IsZero || other.Mantissa.IsZero)
        {
            return Mantissa.Sign.CompareTo(other.Mantissa.Sign);
        }

        // Compare the positions of the leading bits first, so that values of very different size never shift by
        // the exponent difference.
        var order = (long)Mantissa.GetBitLength() + Exponent;
        var otherOrder = (long)other.Mantissa.GetBitLength() + other.Exponent;
        if (order != otherOrder)
        {
            return order.CompareTo(otherOrder);
        }

        return Exponent >= other.Exponent
            ? (Mantissa << (int)(Exponent - other.Exponent)).CompareTo(other.Mantissa)
            : Mantissa.CompareTo(other.Mantissa << (int)(other.Exponent - Exponent));
    }

    public override string ToString() => $"0x{Mantissa:X} * 2^{Exponent}";
}

/// <summary>
/// Independent interval reference for <c>e^q</c> with a dyadic <c>q</c>, and the checks built on it for the big-float
/// <c>Exp</c> and <c>Log</c> tests.
/// </summary>
/// <remarks>
/// <para>
/// Every result is an interval <c>[Lo, Hi]</c> of dyadic rationals that contains the true value: each step rounds the
/// lower bound down and the upper bound up. The oracle uses only <see cref="BigInteger"/> arithmetic and never calls
/// the product code (no product <c>Log</c>, <c>Exp</c>, ln 2 or planner).
/// </para>
/// <para>
/// <c>e^q</c> for <c>q ≥ 0</c>: <c>q' = q / 2^j ≤ 2^-8</c>, a Taylor series summed in fixed point with floored and
/// ceiled terms (the remainder after a term of at most one unit is below one unit and is added to the upper bound),
/// then <c>j</c> squarings. <c>e^-q = 1 / e^q</c> with the bounds swapped.
/// </para>
/// <para>
/// <c>ln x</c> is never evaluated: <c>R = RZ(ln x)</c> is checked through the monotonicity of <c>e^t</c>, as
/// <c>e^R ≤ x &lt; e^(R + ulp)</c> for <c>ln x &gt; 0</c>. The Log checks therefore share no assumption with the
/// product's atanh series.
/// </para>
/// </remarks>
internal static class BigFloatIntervalOracle
{
    // Precision above which a comparison is considered not to converge (a bug in the oracle or a rational e^q).
    private const int MaxPrecisionBits = 1 << 22;

    /// <summary>
    /// Returns bounds <c>Lo ≤ e^q ≤ Hi</c> for <c>q = num / 2^denShift</c>, with a relative width of about
    /// <c>2^-precisionBits</c>.
    /// </summary>
    public static (Dyadic Lo, Dyadic Hi) ExpInterval(BigInteger num, int denShift, int precisionBits)
    {
        if (num.IsZero)
        {
            return (Dyadic.One, Dyadic.One);
        }

        if (num.Sign > 0)
        {
            return ExpIntervalOfPositive(num, denShift, precisionBits);
        }

        var (lo, hi) = ExpIntervalOfPositive(-num, denShift, precisionBits);
        var bits = precisionBits + 64;
        return (ReciprocalDown(hi, bits), ReciprocalUp(lo, bits));
    }

    /// <summary>
    /// Returns <c>RZ_p(e^q)</c> for <c>q = num / 2^denShift</c>: the significand with exactly <paramref name="p"/> bits
    /// (top bit set) and its exponent.
    /// </summary>
    public static (BigInteger Significand, long Exponent) ExpRz(BigInteger num, int denShift, int p)
    {
        if (num.IsZero)
        {
            return (BigInteger.One << (p - 1), -(p - 1));
        }

        // e^q is irrational for a rational q != 0, so the two bounds round to the same value once the interval is
        // narrow enough.
        for (var precision = 2 * p + 64; precision <= MaxPrecisionBits; precision *= 2)
        {
            var (lo, hi) = ExpInterval(num, denShift, precision);
            var low = RoundToBits(lo, p);
            var high = RoundToBits(hi, p);
            if (low == high)
            {
                return (low.Mantissa, low.Exponent);
            }
        }

        throw new InvalidOperationException($"ExpRz did not converge for {num} / 2^{denShift}.");
    }

    /// <summary>
    /// Returns the sign of <c>e^q − x</c> for <c>q = num / 2^denShift</c> and a positive dyadic <paramref name="x"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The bounds do not separate the two values (only possible when
    /// <c>e^q = x</c>, which needs <c>q = 0</c>).</exception>
    public static int CompareExp(BigInteger num, int denShift, Dyadic x, int startPrecisionBits)
    {
        if (num.IsZero)
        {
            return Dyadic.One.CompareTo(x);
        }

        for (var precision = Math.Max(64, startPrecisionBits); precision <= MaxPrecisionBits; precision *= 2)
        {
            var (lo, hi) = ExpInterval(num, denShift, precision);
            if (hi < x)
            {
                return -1;
            }

            if (lo > x)
            {
                return 1;
            }
        }

        throw new InvalidOperationException($"CompareExp did not converge for {num} / 2^{denShift} against {x}.");
    }

    /// <summary>
    /// Returns whether <c>(negative ? -1 : 1) · rSig · 2^rExp</c> is <c>RZ_p(ln x)</c> for <c>x = xSig · 2^xExp &gt; 0</c>,
    /// with <paramref name="rSig"/> normalized to exactly <paramref name="p"/> bits (or zero).
    /// </summary>
    public static bool IsRzOfLog(BigInteger xSig, long xExp, BigInteger rSig, long rExp, bool negative, int p)
    {
        var x = new Dyadic(xSig, xExp);
        var xIsOne = x.CompareTo(Dyadic.One) == 0;
        if (rSig.IsZero)
        {
            // RZ of a non-zero value is non-zero, so 0 is correct only for ln 1.
            return xIsOne;
        }

        if (xIsOne || rSig.GetBitLength() != p)
        {
            return false;
        }

        var (num, denShift) = ToRatio(rSig, rExp);
        var (nextNum, _) = ToRatio(rSig + 1, rExp);
        if (!negative)
        {
            // ln x > 0: R <= ln x < R + ulp, that is e^R <= x < e^(R + ulp). e^R != x for R != 0.
            return CompareExp(num, denShift, x, p + 64) < 0 && CompareExp(nextNum, denShift, x, p + 64) > 0;
        }

        // ln x < 0: R - ulp < ln x <= R, that is e^(R - ulp) < x <= e^R with R = -rSig · 2^rExp.
        return CompareExp(-num, denShift, x, p + 64) > 0 && CompareExp(-nextNum, denShift, x, p + 64) < 0;
    }

    /// <summary>
    /// Returns whether the result is a faithful rounding of <c>ln x</c>: <c>RZ_p(ln x)</c> or the next
    /// <paramref name="p"/>-bit value away from zero.
    /// </summary>
    public static bool IsFaithfulLog(BigInteger xSig, long xExp, BigInteger rSig, long rExp, bool negative, int p)
    {
        if (IsRzOfLog(xSig, xExp, rSig, rExp, negative, p))
        {
            return true;
        }

        if (rSig.IsZero)
        {
            return false;
        }

        var (predSig, predExp) = PredecessorTowardZero(rSig, rExp, p);
        return IsRzOfLog(xSig, xExp, predSig, predExp, negative, p);
    }

    /// <summary>
    /// Returns the next <paramref name="p"/>-bit value toward zero of <c>sig · 2^exp</c> (<paramref name="sig"/> has
    /// exactly <paramref name="p"/> bits).
    /// </summary>
    public static (BigInteger Significand, long Exponent) PredecessorTowardZero(BigInteger sig, long exp, int p) =>
        sig > BigInteger.One << (p - 1)
            ? (sig - 1, exp)
            : ((BigInteger.One << p) - 1, exp - 1);

    /// <summary>
    /// Returns the next <paramref name="p"/>-bit value away from zero of <c>sig · 2^exp</c> (<paramref name="sig"/> has
    /// exactly <paramref name="p"/> bits).
    /// </summary>
    public static (BigInteger Significand, long Exponent) SuccessorAwayFromZero(BigInteger sig, long exp, int p) =>
        sig < (BigInteger.One << p) - 1
            ? (sig + 1, exp)
            : (BigInteger.One << (p - 1), exp + 1);

    /// <summary>
    /// Rounds a non-negative dyadic toward zero to exactly <paramref name="bits"/> significant bits (top bit set); zero
    /// stays zero.
    /// </summary>
    public static Dyadic RoundToBits(Dyadic value, int bits)
    {
        if (value.Mantissa.IsZero)
        {
            return new Dyadic(BigInteger.Zero, 0);
        }

        var length = (long)value.Mantissa.GetBitLength();
        return length >= bits
            ? new Dyadic(value.Mantissa >> (int)(length - bits), value.Exponent + (length - bits))
            : new Dyadic(value.Mantissa << (int)(bits - length), value.Exponent - (bits - length));
    }

    /// <summary>
    /// Writes <c>sig · 2^exp</c> as <c>num / 2^denShift</c> with <c>denShift ≥ 0</c>.
    /// </summary>
    public static (BigInteger Num, int DenShift) ToRatio(BigInteger sig, long exp) =>
        exp >= 0 ? (sig << (int)exp, 0) : (sig, (int)-exp);

    private static (Dyadic Lo, Dyadic Hi) ExpIntervalOfPositive(BigInteger num, int denShift, int precisionBits)
    {
        // q = num / 2^denShift < 2^(length - denShift); q' = q / 2^j <= 2^-8.
        var length = (long)num.GetBitLength();
        var halvings = (int)Math.Max(0, length - denShift + 8);
        var shift = denShift + halvings;

        // Each squaring doubles the relative width, so the series carries j extra bits.
        var workBits = precisionBits + halvings + 64;
        var one = BigInteger.One << workBits;
        var loTerm = one;
        var hiTerm = one;
        var loSum = one;
        var hiSum = one;
        for (var k = 1; ; k++)
        {
            // t_k = t_(k-1) · q' / k, floored for the lower and ceiled for the upper bound.
            var divisor = new BigInteger(k) << shift;
            loTerm = loTerm * num / divisor;
            hiTerm = CeilingDivide(hiTerm * num, divisor);
            loSum += loTerm;
            hiSum += hiTerm;
            if (hiTerm <= 1)
            {
                break;
            }
        }

        // The remainder after a term t_K <= 2^-W is at most t_K · q' / (1 - q') < 2^-W.
        hiSum += 1;

        var lo = new Dyadic(loSum, -workBits);
        var hi = new Dyadic(hiSum, -workBits);
        for (var i = 0; i < halvings; i++)
        {
            lo = RoundDown(lo * lo, workBits);
            hi = RoundUp(hi * hi, workBits);
        }

        return (lo, hi);
    }

    private static Dyadic RoundDown(Dyadic value, int bits)
    {
        var excess = (long)value.Mantissa.GetBitLength() - bits;
        return excess <= 0 ? value : new Dyadic(value.Mantissa >> (int)excess, value.Exponent + excess);
    }

    private static Dyadic RoundUp(Dyadic value, int bits)
    {
        var excess = (long)value.Mantissa.GetBitLength() - bits;
        if (excess <= 0)
        {
            return value;
        }

        var truncated = value.Mantissa >> (int)excess;
        if (truncated << (int)excess != value.Mantissa)
        {
            truncated += 1;
        }

        return new Dyadic(truncated, value.Exponent + excess);
    }

    // 1 / (m · 2^e) = 2^(-e) / m, rounded to about `bits` significant bits.
    private static Dyadic ReciprocalDown(Dyadic value, int bits)
    {
        var scale = bits + (int)value.Mantissa.GetBitLength();
        return new Dyadic((BigInteger.One << scale) / value.Mantissa, -value.Exponent - scale);
    }

    private static Dyadic ReciprocalUp(Dyadic value, int bits)
    {
        var scale = bits + (int)value.Mantissa.GetBitLength();
        return new Dyadic(CeilingDivide(BigInteger.One << scale, value.Mantissa), -value.Exponent - scale);
    }

    private static BigInteger CeilingDivide(BigInteger dividend, BigInteger divisor)
    {
        var quotient = BigInteger.DivRem(dividend, divisor, out var remainder);
        return remainder.IsZero ? quotient : quotient + 1;
    }
}
