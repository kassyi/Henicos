namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// The modulus <c>N(E) = 2^(2E+1) - 2^E + 11 = 2X^2 - X + 11</c> (<c>X = 2^E</c>) that <see cref="FermatAlgorithm"/>
/// works with, and the fold that reduces a square modulo <c>N</c> without a division.
/// </summary>
/// <remarks>
/// <para>Symbols:</para>
/// <list type="bullet">
///   <item><description><c>m = 2E + 1</c> (<see cref="ModulusBits"/>), <c>c = 2^E - 11</c>, so <c>N = 2^m - c</c>.</description></item>
///   <item><description><c>L = ceil(m / 32)</c> words (<see cref="Words"/>). <c>m</c> is odd, so <c>32L &gt;= m + 1</c>.</description></item>
///   <item><description>The fold <c>F(x) = (x mod 2^m) + floor(x / 2^m) * c</c> is congruent to <c>x</c> modulo <c>N</c> and
///   non-negative when <c>c &gt; 0</c>, that is <c>E &gt;= 4</c>.</description></item>
/// </list>
/// <para>
/// The state invariant is <c>0 &lt;= u &lt; 2^(m+1)</c> (redundant: not necessarily below <c>N</c>). For <c>x = u^2</c> three
/// folds give:
/// </para>
/// <list type="number">
///   <item><description><c>q1 &lt; 2^(m+2)</c>, so <c>x1 &lt; 2^m + 2^(m+E+2)</c> (<c>m + E + 3</c> bits, <see cref="FirstFoldWords"/>).</description></item>
///   <item><description><c>q2 &lt;= 2^(E+2)</c>, so <c>x2 &lt; 2^m + 2^(E+2) * 2^E = 3 * 2^m</c> (<c>m + 2</c> bits, <see cref="SecondFoldWords"/>).</description></item>
///   <item><description><c>q3 &lt;= 2</c>, so <c>x3 &lt; 2^m + 2c &lt; 2^m + 2^(E+1)</c>.</description></item>
/// </list>
/// <para>
/// <c>x3 &lt; 2^m + 2^(E+1) &lt;= 2^(m+1)</c> keeps the invariant, and <c>2^m + 2^(E+1) &lt;= 2N</c> holds for every
/// <c>E &gt;= 1</c>, so one conditional subtraction of <c>N</c> (<see cref="Canonicalize"/>) gives the residue in
/// <c>[0, N)</c>. The third fold cannot be skipped: <c>x2</c> can exceed <c>2^(m+1)</c>.
/// </para>
/// </remarks>
internal sealed class QuadraticFoldModulus
{
    /// <summary>The smallest exponent: <c>E = 0</c> gives <c>N = 12</c>, which is even.</summary>
    internal const int MinExponent = 1;

    /// <summary>
    /// The largest exponent, <c>2^27 - 1</c>: a state of <c>L = 2^23</c> words squares to a <c>2^24</c>-word product, the
    /// longest product the NTT multiplication supports (transform length <c>2^25</c>, #529).
    /// </summary>
    internal const int MaxExponent = 134_217_727;

    /// <summary>The smallest exponent with <c>c = 2^E - 11 &gt; 0</c>, below which the fold is not defined.</summary>
    internal const int MinFoldExponent = 4;

    /// <summary>The constant term 11 of <c>N = 2X^2 - X + 11</c>; the fold subtracts <c>11 * q</c> (<c>c = 2^E - 11</c>).</summary>
    internal const uint ConstantTerm = 11;

    private readonly uint[] _modulus;

    /// <summary>Initializes the modulus for exponent <paramref name="exponent"/>.</summary>
    /// <param name="exponent">The exponent <c>E</c>, from <see cref="MinExponent"/> to <see cref="MaxExponent"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is out of range.</exception>
    public QuadraticFoldModulus(int exponent)
    {
        ThrowIfExponentOutOfRange(exponent);

        // In range, 2E + 1 <= 2^28 - 1 and m + E + 34 < 2^29, so the word counts below do not overflow int.
        E = exponent;
        ModulusBits = 2 * exponent + 1;
        Words = WordsOf(exponent);
        ProductWords = 2 * Words;
        FirstFoldWords = (ModulusBits + exponent + 3 + 31) / 32;
        SecondFoldWords = (ModulusBits + 2 + 31) / 32;
        _modulus = BuildModulus(exponent, ModulusBits, Words);
    }

    /// <summary>Gets the exponent <c>E</c>.</summary>
    public int E { get; }

    /// <summary>Gets <c>m = 2E + 1</c>, the bit position of the fold.</summary>
    public int ModulusBits { get; }

    /// <summary>Gets <c>L = ceil(m / 32)</c>, the word count of a state and of a canonical residue.</summary>
    public int Words { get; }

    /// <summary>Gets <c>2L</c>, the word count of the square of a state.</summary>
    public int ProductWords { get; }

    /// <summary>Gets the word count of the first fold's output (<c>m + E + 3</c> bits).</summary>
    public int FirstFoldWords { get; }

    /// <summary>Gets the word count of the second fold's output (<c>m + 2</c> bits).</summary>
    public int SecondFoldWords { get; }

    /// <summary>Gets the words of <c>N</c>, <see cref="Words"/> long.</summary>
    public ReadOnlySpan<uint> Modulus => _modulus;

    /// <summary>Returns <c>ceil((2E + 1) / 32)</c>, the <see cref="Words"/> of the modulus for <paramref name="exponent"/>.</summary>
    /// <param name="exponent">The exponent <c>E</c>.</param>
    public static int WordsOf(int exponent) => (2 * exponent + 1 + 31) / 32;

    /// <summary>Throws when <paramref name="exponent"/> is outside <see cref="MinExponent"/>..<see cref="MaxExponent"/>.</summary>
    /// <param name="exponent">The exponent <c>E</c>.</param>
    /// <param name="paramName">The parameter name to report.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is out of range.</exception>
    public static void ThrowIfExponentOutOfRange(int exponent,
        [CallerArgumentExpression(nameof(exponent))] string? paramName = null)
    {
        if (exponent < MinExponent || exponent > MaxExponent)
        {
            throw new ArgumentOutOfRangeException(paramName, exponent,
                $"E must be between {MinExponent} and {MaxExponent}. E = 0 gives an even N, and above {MaxExponent} the square of a residue exceeds the NTT transform length limit of 2^25 (a 2^24-word product); raising the limit is tracked in #535.");
        }
    }

    /// <summary>
    /// Folds a square into a state: writes <c>F(F(F(x)))</c> of <paramref name="product"/> to <paramref name="result"/>.
    /// </summary>
    /// <param name="product">The square <c>x = u^2</c> of a state <c>u &lt; 2^(m+1)</c>, <see cref="ProductWords"/> words.</param>
    /// <param name="result">Receives a value congruent to <c>x</c> modulo <c>N</c> and below <c>2^m + 2^(E+1)</c> (not
    /// canonical), <see cref="Words"/> words.</param>
    /// <remarks>
    /// The host reference of the GPU kernel <c>GpuBigIntFermatKernels.QuadraticFoldReduceKernel</c>, written as the formula
    /// <c>r + q * 2^E - 11 * q</c> with a signed carry rather than as a copy of the kernel. It is also the fold of the CPU
    /// fallback.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><c>E &lt; 4</c>, where <c>c &lt;= 0</c>.</exception>
    /// <exception cref="ArgumentException">A span has the wrong length.</exception>
    public void FoldReference(ReadOnlySpan<uint> product, Span<uint> result)
    {
        if (E < MinFoldExponent)
        {
            throw new InvalidOperationException(
                $"The fold needs c = 2^E - 11 > 0 (E >= {MinFoldExponent}); E = {E}.");
        }

        ThrowIfLength(product, ProductWords, nameof(product));
        ThrowIfLength(result, Words, nameof(result));

        var first = ArrayPool<uint>.Shared.Rent(FirstFoldWords);
        var second = ArrayPool<uint>.Shared.Rent(SecondFoldWords);
        try
        {
            var firstFold = first.AsSpan(0, FirstFoldWords);
            var secondFold = second.AsSpan(0, SecondFoldWords);
            FoldOnce(product, firstFold);
            FoldOnce(firstFold, secondFold);
            FoldOnce(secondFold, result);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(first);
            ArrayPool<uint>.Shared.Return(second);
        }
    }

    /// <summary>
    /// Reduces a value below <c>2N</c> to its residue in <c>[0, N)</c> by subtracting <c>N</c> at most once.
    /// </summary>
    /// <param name="value">A value below <c>2N</c>, <see cref="Words"/> words; overwritten with its residue.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> has the wrong length.</exception>
    public void Canonicalize(Span<uint> value)
    {
        ThrowIfLength(value, Words, nameof(value));

        if (CompareTo(value, _modulus) >= 0)
        {
            SubtractInPlace(value, _modulus);
        }
    }

    /// <summary>
    /// <c>destination = (x mod 2^m) + q * 2^E - 11 * q</c> with <c>q = floor(x / 2^m)</c>, modulo
    /// <c>2^(32 * destination.Length)</c>; the bounds of the class remarks make the reduction exact.
    /// </summary>
    private void FoldOnce(ReadOnlySpan<uint> source, Span<uint> destination)
    {
        long m = ModulusBits;
        long carry = 0;
        ulong constantTermCarry = 0;
        for (var i = 0; i < destination.Length; i++)
        {
            long word = 32L * i;

            // r_i: bits [32i, 32i + 32) of x below bit m.
            var remainder = ReadBits(source, word, 0) & LowBitsMask(m - word);

            // (q * 2^E)_i: bits [32i - E, 32i - E + 32) of q, which are bits m + 32i - E onward of x, none below m.
            var shiftedQuotient = ReadBits(source, m + word - E, m);

            // (11 * q)_i with its own running carry.
            var quotient = ReadBits(source, m + word, m);
            var constantTermProduct = (ulong)quotient * ConstantTerm + constantTermCarry;
            constantTermCarry = constantTermProduct >> 32;

            var sum = carry + remainder + shiftedQuotient - (uint)constantTermProduct;
            destination[i] = (uint)sum;
            carry = sum >> 32;
        }
    }

    /// <summary>Reads the 32 bits of <paramref name="x"/> from bit <paramref name="start"/> up, reading bits below
    /// <paramref name="floor"/> and past the end as 0 (<paramref name="start"/> &gt;= 0).</summary>
    private static uint ReadBits(ReadOnlySpan<uint> x, long start, long floor)
    {
        var wordIndex = start / 32;
        var bitIndex = (int)(start % 32);
        ulong window = WordAt(x, wordIndex) | ((ulong)WordAt(x, wordIndex + 1) << 32);
        var value = (uint)(window >> bitIndex);
        return start >= floor ? value : value & ~LowBitsMask(floor - start);
    }

    private static uint WordAt(ReadOnlySpan<uint> x, long index) => index < x.Length ? x[(int)index] : 0u;

    /// <summary>A mask of the low <paramref name="bits"/> bits of a word, saturating at 0 and 32.</summary>
    private static uint LowBitsMask(long bits) => bits <= 0 ? 0u : bits >= 32 ? uint.MaxValue : (1u << (int)bits) - 1;

    private static int CompareTo(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        for (var i = left.Length - 1; i >= 0; i--)
        {
            if (left[i] != right[i])
            {
                return left[i] < right[i] ? -1 : 1;
            }
        }

        return 0;
    }

    private static void SubtractInPlace(Span<uint> left, ReadOnlySpan<uint> right)
    {
        long borrow = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var difference = (long)left[i] - right[i] - borrow;
            left[i] = (uint)difference;
            borrow = difference < 0 ? 1 : 0;
        }
    }

    /// <summary>The words of <c>N = (2^m - 2^E) + 11</c>: bits <c>E..m-1</c> set, then 11 added with a carry (for
    /// <c>E &lt;= 3</c> the sum reaches past bit <c>m</c> but still fits the <c>L</c> words).</summary>
    private static uint[] BuildModulus(int exponent, int modulusBits, int words)
    {
        var modulus = new uint[words];
        for (var i = 0; i < words; i++)
        {
            long word = 32L * i;
            modulus[i] = LowBitsMask(modulusBits - word) & ~LowBitsMask(exponent - word);
        }

        ulong carry = ConstantTerm;
        for (var i = 0; i < words && carry != 0; i++)
        {
            var sum = modulus[i] + carry;
            modulus[i] = (uint)sum;
            carry = sum >> 32;
        }

        return modulus;
    }

    private static void ThrowIfLength(ReadOnlySpan<uint> span, int expected, string paramName)
    {
        if (span.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} words, got {span.Length}.", paramName);
        }
    }
}
