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
/// The Log checks never evaluate <c>ln x</c>: <c>R = RZ(ln x)</c> is checked through the monotonicity of <c>e^t</c>,
/// as <c>e^R ≤ x &lt; e^(R + ulp)</c> for <c>ln x &gt; 0</c>. They therefore share no assumption with the product's
/// atanh series.
/// </para>
/// <para>
/// <c>x^y = e^(y·ln x)</c> needs an enclosure of <c>ln x</c>: Halley's iteration on <see cref="ExpInterval"/> finds
/// a center, and <see cref="CompareExp"/> proves <c>e^Lo &lt; x &lt; e^Hi</c> (<see cref="LogInterval"/>). A power that
/// is exactly a <c>p</c>-bit value lies on the boundary of its rounding cell, so no interval separates it; the
/// candidate is then checked exactly as <c>V^(2^k) = x^n</c> for <c>y = n / 2^k</c>, by raising to powers, not by
/// extracting roots as the product does.
/// </para>
/// </remarks>
internal static class BigFloatIntervalOracle
{
    // Precision above which a comparison is considered not to converge (a bug in the oracle or a rational e^q).
    private const int MaxPrecisionBits = 1 << 22;

    // The same for x^y, whose test inputs separate below 2^12 bits: a power that never separates (a bug, or an exact
    // power that IsExactPower misses) then throws within seconds instead of running for hours.
    private const int MaxPowPrecisionBits = 1 << 15;

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
    /// Returns whether <c>e^q</c>, <c>q = num / 2^denShift</c>, lies within <c>radius · 2^unitExponent</c> of
    /// <c>center · 2^centerExponent</c> (positive, the radius below the center).
    /// </summary>
    public static bool IsExpWithin(
        BigInteger num, int denShift, BigInteger center, long centerExponent, BigInteger radius, long unitExponent,
        int startPrecisionBits)
    {
        // [low, high] = (center · 2^(ce - m) -+ radius · 2^(ue - m)) · 2^m with m = min(ce, ue).
        var common = Math.Min(centerExponent, unitExponent);
        var scaledCenter = center << (int)(centerExponent - common);
        var scaledRadius = radius << (int)(unitExponent - common);
        var low = new Dyadic(scaledCenter - scaledRadius, common);
        var high = new Dyadic(scaledCenter + scaledRadius, common);
        for (var precision = Math.Max(64, startPrecisionBits); precision <= MaxPrecisionBits; precision *= 2)
        {
            var (lo, hi) = ExpInterval(num, denShift, precision);
            if (lo >= low && hi <= high)
            {
                return true;
            }

            if (hi < low || lo > high)
            {
                return false;
            }
        }

        throw new InvalidOperationException($"IsExpWithin did not converge for {num} / 2^{denShift}.");
    }

    /// <summary>
    /// Returns whether <c>ln x</c>, <c>x = xSig · 2^xExp &gt; 0</c>, lies within <c>radius · 2^unitExponent</c> of
    /// <c>(negative ? -1 : 1) · center · 2^centerExponent</c>: <c>e^(c − r) ≤ x ≤ e^(c + r)</c>.
    /// </summary>
    public static bool IsLogWithin(
        BigInteger xSig, long xExp, BigInteger center, long centerExponent, bool negative, BigInteger radius,
        long unitExponent, int startPrecisionBits)
    {
        var x = new Dyadic(xSig, xExp);
        var common = Math.Min(centerExponent, unitExponent);
        var scaledCenter = center << (int)(centerExponent - common);
        if (negative)
        {
            scaledCenter = -scaledCenter;
        }

        var scaledRadius = radius << (int)(unitExponent - common);
        var (lowNum, denShift) = ToRatio(scaledCenter - scaledRadius, common);
        var (highNum, _) = ToRatio(scaledCenter + scaledRadius, common);

        // e^t is increasing; e^t = x has no rational solution t != 0 for these x.
        return CompareExp(lowNum, denShift, x, startPrecisionBits) <= 0
               && CompareExp(highNum, denShift, x, startPrecisionBits) >= 0;
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

    /// <summary>
    /// Returns bounds <c>LoNum / 2^DenShift &lt; ln x &lt; HiNum / 2^DenShift</c> for <c>x = xSig · 2^xExp &gt; 0</c>,
    /// with a relative width below <c>2^-precisionBits</c> (both zero for <c>x = 1</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The bounds could not be proven (a bug in the oracle).</exception>
    public static (BigInteger LoNum, BigInteger HiNum, int DenShift) LogInterval(BigInteger xSig, long xExp, int precisionBits)
    {
        var x = new Dyadic(xSig, xExp);
        var common = Math.Min(xExp, 0);
        var distance = BigInteger.Abs((xSig << (int)(xExp - common)) - (BigInteger.One << (int)-common));
        if (distance.IsZero)
        {
            return (BigInteger.Zero, BigInteger.Zero, 0);
        }

        // |ln x| >= |x - 1| / 2 on [1/2, 2] and > 1/2 outside it, so |ln x| >= 2^(top - 2) with |x - 1| < 2^top, and
        // A absolute fraction bits give the relative width.
        var top = (long)distance.GetBitLength() + common;
        var denShift = checked(precisionBits + 32 + (int)Math.Max(0, 2 - top));

        // A double start (about 50 bits), or 0 next to 1, where the first step gives 2(x - 1) / (x + 1).
        var l = BigInteger.Zero;
        if (top > -30)
        {
            var bits = (long)xSig.GetBitLength();
            var shift = (int)Math.Max(0, bits - 53);
            var estimate = Math.Log((double)(xSig >> shift)) + (xExp + shift) * Math.Log(2);
            l = new BigInteger(Math.Round(Math.ScaleB(estimate, 50))) << (denShift - 50);
        }

        // Halley's iteration l <- l + 2(x - e^l) / (x + e^l) converges cubically; e^l comes from the interval of e^l.
        for (var i = 0; i < 64; i++)
        {
            var e = ExpInterval(l, denShift, denShift + 32).Lo;
            var scale = Math.Min(x.Exponent, e.Exponent);
            var xScaled = x.Mantissa << (int)(x.Exponent - scale);
            var eScaled = e.Mantissa << (int)(e.Exponent - scale);
            var step = ((xScaled - eScaled) << (denShift + 1)) / (xScaled + eScaled);
            l += step;
            if (BigInteger.Abs(step) <= 256)
            {
                break;
            }
        }

        // The center is within a few units of ln x; 2^16 units on each side are proven, not assumed.
        var radius = BigInteger.One << 16;
        var lo = l - radius;
        var hi = l + radius;
        if (CompareExp(lo, denShift, x, denShift + 64) >= 0 || CompareExp(hi, denShift, x, denShift + 64) <= 0)
        {
            throw new InvalidOperationException($"LogInterval could not enclose ln({x}).");
        }

        return (lo, hi, denShift);
    }

    /// <summary>
    /// Returns bounds <c>Lo ≤ x^y ≤ Hi</c> for <c>x = xSig · 2^xExp &gt; 0</c> and <c>y = ±ySig · 2^yExp</c>, with a
    /// relative width of about <c>2^-precisionBits</c>.
    /// </summary>
    public static (Dyadic Lo, Dyadic Hi) PowInterval(
        BigInteger xSig, long xExp, BigInteger ySig, long yExp, bool yNegative, int precisionBits)
    {
        if (ySig.IsZero)
        {
            return (Dyadic.One, Dyadic.One);
        }

        // |t| = |y·ln x| < 2^(yTop + bits(|xTop| + 1)): the absolute error of t is the relative error of x^y.
        var yTop = (long)ySig.GetBitLength() + yExp;
        var xTop = (long)xSig.GetBitLength() + xExp;
        var logBits = checked(precisionBits + 32 + (int)Math.Max(0, yTop) + BitLength(Math.Abs(xTop) + 1));
        var (loNum, hiNum, denShift) = LogInterval(xSig, xExp, logBits);
        var (yNum, yDenShift) = ToRatio(ySig, yExp);

        // t = ±y·ln x; a negative y swaps the bounds.
        var tLo = yNegative ? -(yNum * hiNum) : yNum * loNum;
        var tHi = yNegative ? -(yNum * loNum) : yNum * hiNum;
        var tDenShift = checked(denShift + yDenShift);
        return (ExpInterval(tLo, tDenShift, precisionBits + 8).Lo, ExpInterval(tHi, tDenShift, precisionBits + 8).Hi);
    }

    /// <summary>
    /// Returns <c>RZ_p(x^y)</c> for <c>x = xSig · 2^xExp &gt; 0</c> and <c>y = ±ySig · 2^yExp</c>: the significand
    /// with exactly <paramref name="p"/> bits (top bit set) and its exponent.
    /// </summary>
    public static (BigInteger Significand, long Exponent) PowRz(
        BigInteger xSig, long xExp, BigInteger ySig, long yExp, bool yNegative, int p)
    {
        for (var precision = 2 * p + 64; precision <= MaxPowPrecisionBits; precision *= 2)
        {
            var (lo, hi) = PowInterval(xSig, xExp, ySig, yExp, yNegative, precision);
            var low = RoundToBits(lo, p);
            var high = RoundToBits(hi, p);
            if (low == high)
            {
                return (low.Mantissa, low.Exponent);
            }

            // A p-bit x^y is the lower end of its cell, so the bounds round to it and to its predecessor forever.
            if (IsExactPower(high.Mantissa, high.Exponent, xSig, xExp, ySig, yExp, yNegative))
            {
                return (high.Mantissa, high.Exponent);
            }
        }

        throw new InvalidOperationException($"PowRz did not converge for {xSig} * 2^{xExp} ^ {(yNegative ? "-" : "")}{ySig} * 2^{yExp}.");
    }

    /// <summary>
    /// Returns whether <c>±center · 2^centerExponent</c> lies within <c>radius · 2^unitExponent</c> of <c>x^y</c>
    /// (the radius below the center).
    /// </summary>
    public static bool IsPowWithin(
        BigInteger xSig, long xExp, BigInteger ySig, long yExp, bool yNegative, BigInteger center, long centerExponent,
        BigInteger radius, long unitExponent, int startPrecisionBits)
    {
        var common = Math.Min(centerExponent, unitExponent);
        var scaledCenter = center << (int)(centerExponent - common);
        var scaledRadius = radius << (int)(unitExponent - common);
        var low = new Dyadic(scaledCenter - scaledRadius, common);
        var high = new Dyadic(scaledCenter + scaledRadius, common);
        for (var precision = Math.Max(64, startPrecisionBits); precision <= MaxPowPrecisionBits; precision *= 2)
        {
            var (lo, hi) = PowInterval(xSig, xExp, ySig, yExp, yNegative, precision);
            if (lo >= low && hi <= high)
            {
                return true;
            }

            if (hi < low || lo > high)
            {
                return false;
            }
        }

        throw new InvalidOperationException("IsPowWithin did not converge.");
    }

    // Whether V = vSig · 2^vExp equals x^y exactly. With y = n / 2^k (n an integer, k >= 0), V = x^y exactly when
    // V^(2^k) = x^n (n > 0), or V^(2^k) · x^|n| = 1 (n < 0); both sides are compared as odd part and power of two.
    private static bool IsExactPower(
        BigInteger vSig, long vExp, BigInteger xSig, long xExp, BigInteger ySig, long yExp, bool yNegative)
    {
        var (vOdd, vTwos) = OddPart(vSig, vExp);
        var (xOdd, xTwos) = OddPart(xSig, xExp);
        var (yOdd, yTwos) = OddPart(ySig, yExp);

        // A 2^k-th root with k > 62 is never a dyadic (x != 1 here: it needs 2^k | xTwos, or an odd part of 2^62 bits).
        var k = Math.Max(0, -yTwos);
        if (k > 62)
        {
            return false;
        }

        var twoK = BigInteger.One << (int)k;
        var n = yOdd << (int)Math.Max(0, yTwos);
        if (yNegative)
        {
            n = -n;
        }

        if (vTwos * twoK != xTwos * n)
        {
            return false;
        }

        if (n.Sign < 0 || vOdd.IsOne || xOdd.IsOne)
        {
            return vOdd.IsOne && xOdd.IsOne;
        }

        // An odd u > 1 has 2^(bits - 1) < u < 2^bits; disjoint ranges of u^e decide without the powers.
        var vBits = (long)vOdd.GetBitLength();
        var xBits = (long)xOdd.GetBitLength();
        if ((vBits - 1) * twoK >= xBits * n || (xBits - 1) * n >= vBits * twoK)
        {
            return false;
        }

        if (xBits * n > 1 << 24)
        {
            throw new InvalidOperationException("IsExactPower: the powers are too large for the oracle.");
        }

        return BigInteger.Pow(vOdd, (int)twoK) == BigInteger.Pow(xOdd, (int)n);
    }

    // sig · 2^exp = odd · 2^twos.
    private static (BigInteger Odd, long Twos) OddPart(BigInteger sig, long exp)
    {
        var zeros = (int)BigInteger.TrailingZeroCount(sig);
        return (sig >> zeros, exp + zeros);
    }

    private static int BitLength(long value) => 64 - BitOperations.LeadingZeroCount((ulong)value);

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
