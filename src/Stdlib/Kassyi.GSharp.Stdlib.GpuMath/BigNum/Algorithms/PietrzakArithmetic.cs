namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Wraps an <see cref="ISquaringModulus"/> for Pietrzak tree folding and verification while counting modular squarings
/// and preserving <see cref="SquaringRegister.State"/> during prover finalization. Claims, witnesses and endpoints are
/// stated in the signed form <c>|x|</c> (<see cref="SignedResidueForm"/>).
/// </summary>
internal struct PietrzakArithmetic(ISquaringModulus modulus)
{
    private const int ChallengeSquarings = 127;

    private readonly SignedResidueForm _signed = new(modulus.GetModulus(), modulus.Words);

    public long Squarings { get; private set; }

    /// <summary>Returns <c>|x| = min(x, N - x)</c>.</summary>
    public readonly uint[] Abs(uint[] words) => _signed.Abs(words);

    /// <summary>Returns whether a residue below <c>N</c> is in the signed form (at most <c>(N - 1) / 2</c>).</summary>
    public readonly bool IsSigned(ReadOnlySpan<uint> words) => _signed.IsSigned(words);

    public uint[] SquareFrom(ReadOnlySpan<uint> startWords, int squarings)
    {
        modulus.Write(SquaringRegister.State, startWords);
        if (squarings > 0)
        {
            modulus.SquareState(squarings);
            Squarings += squarings;
        }

        return modulus.ReadCanonical(SquaringRegister.State);
    }

    /// <summary>
    /// Computes <c>baseWords^exponent * factorWords mod N</c> for an exponent with bit 127 set, using only
    /// <see cref="SquaringRegister.CheckAux"/>, <see cref="SquaringRegister.CheckD"/> and
    /// <see cref="SquaringRegister.Verdict"/> so chain registers remain untouched.
    /// </summary>
    public uint[] MulPow128(ReadOnlySpan<uint> baseWords, UInt128 exponent, ReadOnlySpan<uint> factorWords)
    {
        modulus.Write(SquaringRegister.CheckAux, baseWords);
        modulus.Copy(SquaringRegister.Verdict, SquaringRegister.CheckAux);

        for (var bit = ChallengeSquarings - 1; bit >= 0; bit--)
        {
            modulus.Square(SquaringRegister.Verdict, 1);
            if (((exponent >> bit) & UInt128.One) != 0)
            {
                modulus.Multiply(SquaringRegister.Verdict, SquaringRegister.Verdict, SquaringRegister.CheckAux);
            }
        }

        Squarings += ChallengeSquarings;
        modulus.Write(SquaringRegister.CheckD, factorWords);
        modulus.Multiply(SquaringRegister.Verdict, SquaringRegister.Verdict, SquaringRegister.CheckD);
        return modulus.ReadCanonical(SquaringRegister.Verdict);
    }

    /// <summary>Returns the next claim <c>(|X^c * mu|, |mu^c * Y|)</c> of a halving round.</summary>
    public PietrzakClaim FoldClaim(PietrzakClaim claim, UInt128 challenge, ReadOnlySpan<uint> witness) =>
        new(
            Abs(MulPow128(claim.X, challenge, witness)),
            Abs(MulPow128(witness, challenge, claim.Y)));

    /// <summary>
    /// Compares <c>|u_K * a^10|</c> with <c>u_E</c>. The signed form leaves the sign of <c>a^(N-1)</c> open, which loses
    /// nothing: <c>a^(N-1) = -1 (mod N)</c> would need every prime factor <c>p</c> of <c>N</c> to have
    /// <c>p = 1 (mod 4)</c>, but <c>N = 3 (mod 4)</c> for <c>E &gt;= 2</c>, and <c>N(1) = 17</c> is prime.
    /// </summary>

    public PietrzakEndpointVerdict ComputeEndpointVerdict(FermatPrpProof proof)
    {
        var uE = proof.ResidueE.Span;
        var uK = proof.ResidueK.Span;
        modulus.Write(SquaringRegister.Final, uK);
        modulus.Write(SquaringRegister.Constant, FermatModulusHelper.ConstantWords(proof.E, proof.Base, modulus, proof.ResidueWords));
        modulus.Multiply(SquaringRegister.Verdict, SquaringRegister.Final, SquaringRegister.Constant);
        var verdictResidue = Abs(modulus.ReadCanonical(SquaringRegister.Verdict));
        return new PietrzakEndpointVerdict(
            verdictResidue.AsSpan().SequenceEqual(uE),
            FermatModulusHelper.Low64(verdictResidue),
            FermatModulusHelper.Low64(uE));
    }
}
