namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// The static result widths of the big integer operations: from upper bounds of the operands in bits, an upper bound of
/// the result in bits, which an expression knows when it is built.
/// </summary>
/// <remarks>
/// <para>
/// Bounds are bits, not words, so that a chain of additions grows by about one bit per addition instead of one word.
/// A bound is at least 1; a buffer holds <see cref="Words"/> of it. A leaf of <c>n</c> words has the bound
/// <c>32 n</c>.
/// </para>
/// <para>
/// The rules follow GMP (<c>mpz/aors.h</c> reserves the larger size plus one limb for a sum, <c>mpz/mul.c</c> the
/// sum of the sizes for a product, <c>mpz/com.c</c> one more limb for <c>~x</c> of a non-negative <c>x</c>) and .NET
/// <c>BigInteger</c>, which shifts by a negative count in the other direction.
/// </para>
/// </remarks>
internal static class BigIntWidths
{
    /// <summary>The bits of one word.</summary>
    public const int BitsPerWord = 32;

    /// <summary>The words that hold a bound: <c>max(1, ceil(bits / 32))</c>.</summary>
    /// <param name="bits">The bound in bits.</param>
    /// <returns>The word count.</returns>
    /// <exception cref="OverflowException">The word count does not fit in an <see cref="int"/>.</exception>
    public static int Words(long bits) =>
        bits <= BitsPerWord ? 1 : checked((int)((bits + BitsPerWord - 1) / BitsPerWord));

    /// <summary>The bound of a buffer of <paramref name="words"/> words.</summary>
    /// <param name="words">The word count, at least 1.</param>
    /// <returns><c>32 * words</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="words"/> is below 1.</exception>
    public static long OfWords(int words)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        return (long)BitsPerWord * words;
    }

    /// <summary><c>a + b</c> (unsigned, or the magnitudes of a signed sum): <c>max(a, b) + 1</c>.</summary>
    public static long Add(long a, long b) => checked(Math.Max(a, b) + 1);

    /// <summary>Unsigned <c>a - b</c>: <c>a</c>; a result below zero is a domain error.</summary>
    public static long UnsignedSubtract(long a, long b) => a;

    /// <summary>Signed <c>a - b</c>: <c>max(a, b) + 1</c>, as <see cref="Add"/>.</summary>
    public static long SignedSubtract(long a, long b) => Add(a, b);

    /// <summary><c>a * b</c>: <c>a + b</c>.</summary>
    public static long Multiply(long a, long b) => checked(a + b);

    /// <summary><c>a / b</c>: <c>a</c>; the quotient is at most the dividend.</summary>
    public static long Divide(long a, long b) => a;

    /// <summary><c>a % b</c>: <c>min(a, b)</c>; the remainder is below the divisor and at most the dividend.</summary>
    public static long Remainder(long a, long b) => Math.Min(a, b);

    /// <summary><c>a &lt;&lt; count</c>: <c>a + count</c>; a negative count shifts right.</summary>
    public static long ShiftLeft(long a, long count) => count >= 0 ? checked(a + count) : ShiftRightBy(a, -count);

    /// <summary><c>a &gt;&gt; count</c>: <c>max(a - count, 1)</c>; a negative count shifts left.</summary>
    public static long ShiftRight(long a, long count) => count >= 0 ? ShiftRightBy(a, count) : checked(a - count);

    /// <summary>Signed <c>~a</c> (<c>-a - 1</c>): <c>a + 1</c>.</summary>
    public static long SignedNot(long a) => checked(a + 1);

    /// <summary>Unsigned <c>~a</c>: every bit of the words of <c>a</c>, <c>32 * Words(a)</c>.</summary>
    public static long UnsignedNot(long a) => OfWords(Words(a));

    /// <summary>Unsigned <c>a &amp; b</c>: <c>min(a, b)</c>.</summary>
    public static long And(long a, long b) => Math.Min(a, b);

    /// <summary>Unsigned <c>a | b</c>: <c>max(a, b)</c>.</summary>
    public static long Or(long a, long b) => Math.Max(a, b);

    /// <summary>Unsigned <c>a ^ b</c>: <c>max(a, b)</c>.</summary>
    public static long Xor(long a, long b) => Math.Max(a, b);

    /// <summary><c>floor(sqrt(a))</c>: <c>ceil(a / 2)</c>.</summary>
    public static long Sqrt(long a) => (a + 1) / 2;

    // Shifting right by a non-negative count; count may be 2^31 (a left shift by int.MinValue)
    private static long ShiftRightBy(long a, long count) => Math.Max(a - count, 1);
}
