namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Independent <see cref="BigInteger"/> reference for big-float values <c>sign * sig * 2^exp</c>.
/// </summary>
/// <remarks>
/// Values are compared as exact rationals (numerator / denominator), never through <see cref="double"/>.
/// The transcendental references use fixed-point arithmetic with <see cref="FractionBits"/> fraction bits,
/// far more than the 128 bits that the tests check, so their own truncation error does not matter.
/// </remarks>
internal static class BigFloatReference
{
    /// <summary>Fraction bits of the fixed-point references.</summary>
    public const int FractionBits = 400;

    /// <summary>
    /// Returns whether <c>sign * sig * 2^exp</c> equals <c>num / den</c> exactly.
    /// </summary>
    public static bool EqualsRational(BigInteger sig, int exp, bool isNegative, BigInteger num, BigInteger den)
    {
        var signedSig = isNegative ? -sig : sig;
        // sig * 2^exp * den == num  <=>  sig * den * 2^max(exp, 0) == num * 2^max(-exp, 0)
        var left = signedSig * den << Math.Max(exp, 0);
        var right = num << Math.Max(-exp, 0);
        return left == right;
    }

    /// <summary>
    /// Returns <c>RZ_p(num / den)</c> for <c>num ≥ 0</c> and <c>den &gt; 0</c>: the significand with exactly
    /// <paramref name="p"/> bits (top bit set) and its exponent, so that <c>sig·2^exp ≤ num / den &lt; (sig + 1)·2^exp</c>;
    /// <c>(0, 0)</c> for <c>num = 0</c>.
    /// </summary>
    public static (BigInteger Significand, long Exponent) RoundTowardZero(BigInteger num, BigInteger den, int p)
    {
        if (num.Sign < 0 || den.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(num), "RoundTowardZero takes num >= 0 and den > 0.");
        }

        if (num.IsZero)
        {
            return (BigInteger.Zero, 0);
        }

        // num / den lies in (2^(bn - bd - 1), 2^(bn - bd + 1)), so floor(num / (den·2^e)) has p or p + 1 bits for
        // e = bn - bd - p; floor(floor(x) / 2) = floor(x / 2) drops the extra bit without rounding twice.
        var exponent = (long)num.GetBitLength() - (long)den.GetBitLength() - p;
        var significand = exponent >= 0 ? num / (den << (int)exponent) : (num << (int)-exponent) / den;
        if (significand.GetBitLength() > p)
        {
            significand >>= 1;
            exponent++;
        }

        return (significand, exponent);
    }

    /// <summary>
    /// Returns whether <c>sig·2^exp</c> is <c>RZ_p(num / den)</c> by the definition: <paramref name="sig"/> has exactly
    /// <paramref name="p"/> bits and <c>sig·2^exp ≤ num / den &lt; (sig + 1)·2^exp</c>, or both are zero.
    /// </summary>
    public static bool IsRoundedTowardZero(BigInteger sig, long exp, BigInteger num, BigInteger den, int p)
    {
        if (num.IsZero || sig.IsZero)
        {
            return num.IsZero && sig.IsZero;
        }

        if (sig.GetBitLength() != p)
        {
            return false;
        }

        // sig·2^exp·den <= num < (sig + 1)·2^exp·den, both sides scaled by 2^max(-exp, 0).
        var scaledNum = exp >= 0 ? num : num << (int)-exp;
        var scaledDen = exp >= 0 ? den << (int)exp : den;
        return sig * scaledDen <= scaledNum && scaledNum < (sig + 1) * scaledDen;
    }

    /// <summary>
    /// Returns the sign (-1, 0 or 1) of the exact sum of <c>c·2^e</c> over <paramref name="terms"/> without building
    /// it: a term entirely below the lowest bit of the partial sum of the higher terms cannot change its sign, so
    /// exponents <c>2^32</c> apart need no shift of <c>2^32</c> bits.
    /// </summary>
    public static int SignOfSum(params (BigInteger Coefficient, long Exponent)[] terms)
    {
        // The nonzero terms by the position just above their top bit, highest first.
        var sorted = terms
            .Where(static t => !t.Coefficient.IsZero)
            .OrderByDescending(static t => t.Exponent + (long)BigInteger.Abs(t.Coefficient).GetBitLength())
            .ToArray();
        var sum = BigInteger.Zero;
        var sumExponent = 0L;
        for (var i = 0; i < sorted.Length; i++)
        {
            var (coefficient, exponent) = sorted[i];
            if (sum.IsZero)
            {
                (sum, sumExponent) = (coefficient, exponent);
                continue;
            }

            // The remaining k terms are each below 2^top, so together below k·2^top <= 2^sumExponent <= |sum|.
            var top = exponent + (long)BigInteger.Abs(coefficient).GetBitLength();
            var remaining = sorted.Length - i;
            if (top + (long)new BigInteger(remaining - 1).GetBitLength() <= sumExponent)
            {
                return sum.Sign;
            }

            // The spans overlap or nearly so, so the alignment shift is bounded by the widths of the terms.
            var low = Math.Min(sumExponent, exponent);
            sum = (sum << checked((int)(sumExponent - low))) + (coefficient << checked((int)(exponent - low)));
            sumExponent = low;
        }

        return sum.Sign;
    }

    /// <summary>
    /// Returns whether <c>±sig·2^exp</c> is <c>RZ_p</c> of the exact sum of <c>c·2^e</c> over <paramref name="terms"/>
    /// by the definition (<paramref name="sig"/> of exactly <paramref name="p"/> bits, the sign of the sum, and
    /// <c>sig·2^exp ≤ |v| &lt; (sig + 1)·2^exp</c>), or both are zero, through <see cref="SignOfSum"/> only.
    /// </summary>
    public static bool IsRoundedTowardZeroOfSum(
        BigInteger sig, long exp, bool isNegative, int p, params (BigInteger Coefficient, long Exponent)[] terms)
    {
        var sign = SignOfSum(terms);
        if (sign == 0 || sig.IsZero)
        {
            return sign == 0 && sig.IsZero && !isNegative;
        }

        if (sig.GetBitLength() != p || isNegative != sign < 0)
        {
            return false;
        }

        // |v| - sig·2^exp >= 0 and (sig + 1)·2^exp - |v| > 0, with |v| = sign·v.
        var lower = new (BigInteger Coefficient, long Exponent)[terms.Length + 1];
        var upper = new (BigInteger Coefficient, long Exponent)[terms.Length + 1];
        for (var i = 0; i < terms.Length; i++)
        {
            lower[i] = (sign * terms[i].Coefficient, terms[i].Exponent);
            upper[i] = (-sign * terms[i].Coefficient, terms[i].Exponent);
        }

        lower[^1] = (-sig, exp);
        upper[^1] = (sig + 1, exp);
        return SignOfSum(lower) >= 0 && SignOfSum(upper) > 0;
    }

    /// <summary>
    /// Formats <c>sign * sig * 2^exp</c> for an assertion message.
    /// </summary>
    public static string Describe(BigInteger sig, int exp, bool isNegative) =>
        $"{(isNegative ? "-" : "+")}0x{sig:X} * 2^{exp}";

    /// <summary>
    /// Returns <c>floor(sig * 2^(exp + FractionBits))</c>.
    /// </summary>
    public static BigInteger ToFixed(BigInteger sig, int exp)
    {
        var shift = exp + FractionBits;
        return shift >= 0 ? sig << shift : sig >> -shift;
    }

    /// <summary>
    /// Returns <c>floor(sqrt(n))</c> (Newton iteration on integers), checked against its definition
    /// <c>r² ≤ n &lt; (r + 1)²</c>; it does not use the product's square roots.
    /// </summary>
    public static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "IntegerSqrt takes n >= 0.");
        }

        if (n.IsZero)
        {
            return 0;
        }

        // Newton from above, x >= sqrt(n), decreases until it reaches floor(sqrt(n)).
        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + n / x) >> 1;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        if (x * x > n || (x + 1) * (x + 1) <= n)
        {
            throw new InvalidOperationException($"IntegerSqrt: {x} is not floor(sqrt({n})).");
        }

        return x;
    }

    /// <summary>
    /// Returns <c>RZ_p(sqrt(sig·2^exp))</c> for <c>sig &gt; 0</c>: the significand with exactly <paramref name="p"/>
    /// bits and its exponent (<c>(0, 0)</c> for <c>sig = 0</c>).
    /// </summary>
    public static (BigInteger Significand, long Exponent) RzSqrt(BigInteger sig, long exp, int p)
    {
        if (sig.IsZero)
        {
            return (BigInteger.Zero, 0);
        }

        // sqrt(sig·2^exp) = sqrt(sig·2^t)·2^((exp - t) / 2) with exp - t even and sig·2^t in [2^(2p-2), 2^(2p)), so
        // floor(sqrt(sig·2^t)) has exactly p bits; floor(sqrt(floor(y))) = floor(sqrt(y)) lets a negative t truncate.
        var t = 2L * p - (long)sig.GetBitLength();
        if (((exp - t) & 1) != 0)
        {
            t--;
        }

        var scaled = t >= 0 ? sig << (int)t : sig >> (int)-t;
        return (IntegerSqrt(scaled), (exp - t) / 2);
    }

    /// <summary>
    /// Returns <c>RZ_p(1 / sqrt(sig·2^exp))</c> for <c>sig &gt; 0</c>: the significand with exactly
    /// <paramref name="p"/> bits and its exponent.
    /// </summary>
    public static (BigInteger Significand, long Exponent) RzRsqrt(BigInteger sig, long exp, int p)
    {
        if (sig.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sig), "RzRsqrt takes sig > 0.");
        }

        // An even exponent: 1 / sqrt(s·2^e) = 2^(-e/2 - k)·(2^k / sqrt(s)).
        var odd = (exp & 1) != 0;
        var s = odd ? sig << 1 : sig;
        var e = odd ? exp - 1 : exp;

        // y = floor(2^k / sqrt(s)) = floor(sqrt(floor(2^(2k) / s))), and 2^k / sqrt(s) > 2^(p + 1) for this k, so
        // dropping the low bits of y truncates the real value (floor(floor(x) / 2^d) = floor(x / 2^d)).
        var k = p + (int)((s.GetBitLength() + 1) / 2) + 1;
        var y = IntegerSqrt((BigInteger.One << (2 * k)) / s);
        var drop = (int)y.GetBitLength() - p;
        return (y >> drop, -e / 2 - k + drop);
    }

    /// <summary>
    /// Returns <c>sqrt(v)</c> for a positive integer <paramref name="v"/> in fixed point.
    /// </summary>
    public static BigInteger SqrtFixed(uint v) => IntegerSqrt(new BigInteger(v) << (2 * FractionBits));

    /// <summary>
    /// Returns <c>1 / sqrt(v)</c> for a positive integer <paramref name="v"/> in fixed point.
    /// </summary>
    public static BigInteger InverseSqrtFixed(uint v) => (BigInteger.One << (2 * FractionBits)) / SqrtFixed(v);

    /// <summary>
    /// Returns <c>e^x</c> for an integer <paramref name="x"/> in fixed point (Taylor series of e^|x|; e^-|x| = 1 / e^|x|).
    /// </summary>
    public static BigInteger ExpFixed(int x)
    {
        if (x < 0)
        {
            return (BigInteger.One << (2 * FractionBits)) / ExpFixed(-x);
        }

        var one = BigInteger.One << FractionBits;
        var term = one;
        var sum = one;
        for (var k = 1; !term.IsZero; k++)
        {
            term = term * x / k;
            sum += term;
        }

        return sum;
    }

    /// <summary>
    /// Returns <c>ln 2 = sum_{k>=1} 1 / (k * 2^k)</c> in fixed point.
    /// </summary>
    public static BigInteger Ln2Fixed()
    {
        var one = BigInteger.One << FractionBits;
        BigInteger sum = 0;
        for (var k = 1; k <= FractionBits + 8; k++)
        {
            sum += one / (k * (BigInteger.One << k));
        }

        return sum;
    }

    /// <summary>
    /// Returns whether <paramref name="actual"/> is within a relative error of <c>2^-bits</c> of <paramref name="expected"/>
    /// (both in the same fixed point).
    /// </summary>
    public static bool IsWithinRelative(BigInteger actual, BigInteger expected, int bits) =>
        BigInteger.Abs(actual - expected) << bits <= BigInteger.Abs(expected);
}
