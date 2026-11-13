namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Metadata header identifying the modulus <c>N(E) = 2^(2E+1) - 2^E + 11</c>, base <c>a</c>, Pietrzak halving depth
/// <c>k</c> and canonical residue word count of a <see cref="FermatPrpProof"/>.
/// </summary>
/// <param name="E">The exponent <c>E</c> of <c>N = 2^(2E+1) - 2^E + 11</c>.</param>
/// <param name="Base">The base <c>a</c>.</param>
/// <param name="ProofPower">The halving tree depth <c>k</c> (<c>2^k</c> leaves per segment).</param>
/// <param name="ResidueWords">The canonical word count <c>ceil((2E + 1) / 32)</c> of each residue.</param>
public readonly record struct FermatPrpProofHeader(int E, uint Base, int ProofPower, int ResidueWords)
{
    /// <summary>
    /// Initializes a proof header deriving <see cref="ResidueWords"/> from <paramref name="e"/> when in range.
    /// </summary>
    /// <param name="e">The exponent <c>E</c>.</param>
    /// <param name="base">The base <c>a</c>.</param>
    /// <param name="proofPower">The halving tree depth <c>k</c>.</param>
    public FermatPrpProofHeader(int e, uint @base, int proofPower)
        : this(
            e,
            @base,
            proofPower,
            e >= QuadraticFoldModulus.MinExponent && e <= QuadraticFoldModulus.MaxExponent
                ? QuadraticFoldModulus.WordsOf(e)
                : 0)
    {
    }
}
