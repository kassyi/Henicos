namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Shared modulus, starting residue and divisor helpers for <see cref="FermatAlgorithm"/> and
/// <see cref="PietrzakProofVerifier"/>.
/// </summary>
internal static class FermatModulusHelper
{
    /// <summary>
    /// Returns <c>a^10 mod N</c> in <paramref name="words"/> words without building <c>N</c> for large exponents:
    /// when <c>E &gt;= 161</c>, <c>N &gt; 2^322 &gt; (2^32 - 1)^10 &gt;= a^10</c>.
    /// </summary>
    public static uint[] ConstantWords(int e, uint @base, ISquaringModulus modulus, int words)
    {
        var power10 = e >= 161
            ? BigInteger.Pow(@base, 10)
            : BigInteger.ModPow(@base, 10, modulus.GetModulus());
        return ToWords(power10, words);
    }

    /// <summary>
    /// Returns <c>u_0 = a mod N</c> in <paramref name="words"/> words without building <c>N</c>: for <c>E &gt;= 32</c>,
    /// <c>N &gt; 2^64 &gt; a</c>.
    /// </summary>
    public static uint[] StartWords(int e, uint @base, int words)
    {
        var start = new uint[words];
        ulong value = @base;
        if (e <= QuadraticHostModulus.MaxExponent)
        {
            value %= (1UL << (2 * e + 1)) - (1UL << e) + QuadraticFoldModulus.ConstantTerm;
        }

        start[0] = (uint)value;
        if (words > 1)
        {
            start[1] = (uint)(value >> 32);
        }

        return start;
    }

    public static ISquaringModulus CreateModulus(int e, bool isCpuFallback) =>
        e <= QuadraticHostModulus.MaxExponent
            ? new QuadraticHostModulus(e)
            : new QuadraticDeviceModulus(new QuadraticFoldModulus(e), isCpuFallback);

    public static bool IsTrivialBase(int e, uint @base, out ulong modulus, out ulong reduced)
    {
        modulus = (1UL << (2 * e + 1)) - (1UL << e) + QuadraticFoldModulus.ConstantTerm;
        reduced = @base % modulus;
        return reduced == 0 || reduced == 1 || reduced == modulus - 1;
    }

    /// <summary>Returns <c>gcd(a, N)</c> when it is not 1, from <c>N mod a = 2x^2 - x + 11 mod a</c> with <c>x = 2^E mod a</c>.</summary>
    public static uint? SmallDivisor(int e, uint @base)
    {
        ulong x = 1;
        ulong power = 2 % @base;
        for (var bits = e; bits > 0; bits >>= 1)
        {
            if ((bits & 1) != 0)
            {
                x = x * power % @base;
            }

            power = power * power % @base;
        }

        // 2x^2 + 11 + (a - x) is non-negative because x < a.
        var nModBase = (ulong)(((UInt128)2 * x * x + QuadraticFoldModulus.ConstantTerm + (@base - x)) % @base);
        var gcd = (ulong)BigInteger.GreatestCommonDivisor(@base, nModBase);
        return gcd == 1 ? null : (uint)gcd;
    }

    public static ulong Low64(ReadOnlySpan<uint> words) =>
        words.Length > 1 ? words[0] | ((ulong)words[1] << 32) : words[0];

    private static uint[] ToWords(BigInteger value, int words)
    {
        var result = new uint[words];
        value.TryWriteBytes(MemoryMarshal.AsBytes(result.AsSpan()), out _, isUnsigned: true);
        return result;
    }
}

