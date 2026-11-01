namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>Holds the Fermat probable-prime congruence outcome computed on verified endpoints.</summary>
internal readonly record struct PietrzakEndpointVerdict(bool IsProbablePrime, ulong Residue64, ulong CheckResidue64);

/// <summary>
/// Wraps an <see cref="ISquaringModulus"/> for Pietrzak tree folding and verification while counting modular squarings
/// and preserving <see cref="SquaringRegister.State"/> during prover finalization.
/// </summary>
internal struct PietrzakArithmetic(ISquaringModulus modulus)
{
    private const int ChallengeSquarings = 127;

    public long Squarings { get; private set; }

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

    public PietrzakClaim FoldClaim(PietrzakClaim claim, UInt128 challenge, ReadOnlySpan<uint> witness) =>
        new(
            MulPow128(claim.X, challenge, witness),
            MulPow128(witness, challenge, claim.Y));

    public PietrzakEndpointVerdict ComputeEndpointVerdict(FermatPrpProof proof)
    {
        var uE = proof.ResidueE.Span;
        var uK = proof.ResidueK.Span;
        modulus.Write(SquaringRegister.Final, uK);
        modulus.Write(SquaringRegister.Constant, FermatAlgorithm.ConstantWords(proof.E, proof.Base, modulus, proof.ResidueWords));
        modulus.Multiply(SquaringRegister.Verdict, SquaringRegister.Final, SquaringRegister.Constant);
        var verdictResidue = modulus.ReadCanonical(SquaringRegister.Verdict);
        return new PietrzakEndpointVerdict(
            verdictResidue.AsSpan().SequenceEqual(uE),
            FermatAlgorithm.Low64(verdictResidue),
            FermatAlgorithm.Low64(uE));
    }
}

