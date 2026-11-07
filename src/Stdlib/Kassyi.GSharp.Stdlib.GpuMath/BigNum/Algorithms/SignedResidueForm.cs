namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// The signed form <c>|x| = min(x, N - x)</c> of residues modulo <c>N</c>, in which a Pietrzak proof states and
/// hashes its values, so that <c>x</c> and <c>-x</c> have one encoding.
/// </summary>
/// <remarks>
/// <para>
/// The proof works in <c>Z_N^* / {+-1}</c>, written as <c>|x|</c> with <c>x</c> in <c>[-(N-1)/2, (N-1)/2]</c>. For a
/// prime <c>N = 3 (mod 4)</c>, which every <c>N(E)</c> with <c>E &gt;= 2</c> is when prime, that group is isomorphic
/// to <c>QR_N</c>, of odd order <c>(N-1)/2</c>. Pietrzak ("Simple Verifiable Delay Functions", ITCS 2019, section 2.1)
/// uses the signed quadratic residues <c>QR_N^+</c> of an RSA modulus, whose membership test is the range check and a
/// Jacobi symbol of +1. The Jacobi test is left out here: for a prime <c>N = 3 (mod 4)</c>, <c>(-1/N) = -1</c>, so one
/// of <c>x</c> and <c>-x</c> always has symbol +1 and the range check <see cref="IsSigned"/> alone picks the encoding.
/// </para>
/// <para>
/// The form removes the element <c>-1</c> of order 2: in <c>Z_N^*</c> a prover could negate a midpoint and the next
/// witness together, keep every halving check true and flip the sign of the claimed end value. It does not remove
/// other elements of small order. Pietrzak's soundness argument needs safe primes (section 2.3), which <c>N(E)</c> is
/// not, so soundness against a prover that uses an element of small odd order is not claimed, and for a composite
/// <c>N</c> the non-trivial square roots of 1 still have order 2 in this group.
/// </para>
/// </remarks>
internal sealed class SignedResidueForm
{
    private readonly uint[] _modulus;
    private readonly uint[] _halfModulus;

    /// <summary>Initializes the form for an odd modulus <paramref name="modulus"/>.</summary>
    /// <param name="modulus">The odd modulus <c>N</c>.</param>
    /// <param name="words">The word count of a residue.</param>
    public SignedResidueForm(BigInteger modulus, int words)
    {
        _modulus = FermatModulusHelper.ToWords(modulus, words);
        _halfModulus = FermatModulusHelper.ToWords(modulus >> 1, words);
    }

    /// <summary>
    /// Returns whether <paramref name="words"/> is at most <c>(N - 1) / 2</c>, the range of the signed form.
    /// </summary>
    /// <param name="words">A residue below <c>N</c>.</param>
    /// <returns><see langword="true"/> when the value is its own <c>|x|</c>.</returns>
    public bool IsSigned(ReadOnlySpan<uint> words) => Compare(words, _halfModulus) <= 0;

    /// <summary>Returns <c>|x| = min(x, N - x)</c>.</summary>
    /// <param name="words">A residue below <c>N</c>; returned as it is when already in the signed form.</param>
    /// <returns><paramref name="words"/>, or a new array holding <c>N - x</c>.</returns>
    public uint[] Abs(uint[] words)
    {
        if (IsSigned(words))
        {
            return words;
        }

        var result = new uint[words.Length];
        long borrow = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var difference = (long)_modulus[i] - words[i] - borrow;
            result[i] = (uint)difference;
            borrow = difference < 0 ? 1 : 0;
        }

        return result;
    }

    private static int Compare(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
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
}
