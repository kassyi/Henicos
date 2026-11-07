namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// The signed form <c>|x| = min(x, N - x)</c> of residues modulo <c>N</c>, in which a Pietrzak proof states and hashes its
/// values, so that <c>x</c> and <c>-x</c> have one encoding.
/// </summary>
/// <remarks>
/// Pietrzak runs the protocol in the signed quadratic residues <c>QR_N^+</c> ("Simple Verifiable Delay Functions",
/// ITCS 2019, section 2.1): every value is written as <c>|x|</c> with <c>x</c> in <c>[-(N-1)/2, (N-1)/2]</c>, membership is
/// the range check <see cref="IsSigned"/>, and the proof becomes unique. Without it the element <c>-1</c> of order 2 lets
/// a prover negate a midpoint and the next witness together: every halving check still holds, but the claimed end
/// value changes sign.
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

    /// <summary>Returns whether <paramref name="words"/> is at most <c>(N - 1) / 2</c>, the range of the signed form.</summary>
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
