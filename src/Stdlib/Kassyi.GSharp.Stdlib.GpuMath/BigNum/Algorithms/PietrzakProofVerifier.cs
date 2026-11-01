namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>Verifies dual-segment batched <see cref="FermatPrpProof"/> instances.</summary>
internal static class PietrzakProofVerifier
{
    public static FermatPrpVerificationResult Verify(FermatPrpProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        var stopwatch = Stopwatch.StartNew();
        var header = proof.Header;

        if (ValidateMetadata(proof) is { } metadataError)
        {
            return FermatPrpVerificationResult.Invalid(0, stopwatch.Elapsed, metadataError);
        }

        using var modulus = FermatModulusHelper.CreateModulus(header.E, GpuKernelLoader.IsCpuFallbackMode);
        if (ValidateResidues(proof, modulus) is { } residueError)
        {
            return FermatPrpVerificationResult.Invalid(0, stopwatch.Elapsed, residueError);
        }

        var schedule = new PietrzakSchedule(header);
        var math = new PietrzakArithmetic(modulus);
        var uE = proof.ResidueE.Span;
        var uK = proof.ResidueK.Span;

        var startWords = FermatModulusHelper.StartWords(header.E, header.Base, header.ResidueWords);
        var a0 = math.SquareFrom(startWords, schedule.Remainder);
        var b0 = math.SquareFrom(uE, 1 + schedule.Remainder);

        var alpha = schedule.DeriveMergeChallenge(uE, uK);
        var claim = new PietrzakClaim(
            math.MulPow128(b0, alpha, a0),
            math.MulPow128(uK, alpha, uE));

        for (var round = 0; round < schedule.ProofPower; round++)
        {
            var witness = proof.Witnesses[round].Span;
            var challenge = schedule.DeriveRoundChallenge(round, claim, witness);
            claim = math.FoldClaim(claim, challenge, witness);
        }

        var finalX = math.SquareFrom(claim.X, schedule.StepSize);
        if (IsZero(finalX) || IsZero(claim.Y) || !finalX.AsSpan().SequenceEqual(claim.Y))
        {
            return FermatPrpVerificationResult.Invalid(
                math.Squarings,
                stopwatch.Elapsed,
                "Final Pietrzak check X^(2^d) == Y (mod N) failed.");
        }

        var verdict = math.ComputeEndpointVerdict(proof);
        return FermatPrpVerificationResult.Valid(verdict, math.Squarings, stopwatch.Elapsed);
    }

    private static string? ValidateMetadata(FermatPrpProof proof)
    {
        var header = proof.Header;
        if (header.E < QuadraticFoldModulus.MinExponent || header.E > FermatAlgorithm.MaxExponent)
        {
            return $"Exponent E = {header.E} is outside [{QuadraticFoldModulus.MinExponent}, {FermatAlgorithm.MaxExponent}].";
        }

        if (header.Base < 2)
        {
            return $"Base {header.Base} must be at least 2.";
        }

        if (header.ProofPower < 1 || header.ProofPower > FermatPrpProof.MaxProofPower || (1L << header.ProofPower) > header.E)
        {
            return $"ProofPower {header.ProofPower} must be in [1, {FermatPrpProof.MaxProofPower}] with 2^ProofPower <= E ({header.E}).";
        }

        var expectedWords = QuadraticFoldModulus.WordsOf(header.E);
        if (header.ResidueWords != expectedWords)
        {
            return $"ResidueWords {header.ResidueWords} does not match expected {expectedWords}.";
        }

        if (proof.Witnesses.Count != header.ProofPower)
        {
            return $"Expected {header.ProofPower} witnesses, got {proof.Witnesses.Count}.";
        }

        if (header.E <= QuadraticHostModulus.MaxExponent && FermatModulusHelper.IsTrivialBase(header.E, header.Base, out _, out _))
        {
            return $"Base {header.Base} is trivial modulo N({header.E}).";
        }

        if (FermatModulusHelper.SmallDivisor(header.E, header.Base) is { } divisor)
        {
            return $"Base {header.Base} shares divisor {divisor} with N({header.E}).";
        }

        return null;
    }

    private static string? ValidateResidues(FermatPrpProof proof, ISquaringModulus modulus)
    {
        var words = proof.ResidueWords;
        if (!IsNonZeroCanonicalResidue(proof.ResidueE.Span, words, modulus))
        {
            return "ResidueE must be a non-zero canonical residue below N.";
        }

        if (!IsNonZeroCanonicalResidue(proof.ResidueK.Span, words, modulus))
        {
            return "ResidueK must be a non-zero canonical residue below N.";
        }

        for (var t = 0; t < proof.ProofPower; t++)
        {
            if (!IsNonZeroCanonicalResidue(proof.Witnesses[t].Span, words, modulus))
            {
                return $"Witness mu_{t} must be a non-zero canonical residue below N.";
            }
        }

        return null;
    }

    private static bool IsNonZeroCanonicalResidue(ReadOnlySpan<uint> words, int expectedWords, ISquaringModulus modulus) =>
        words.Length == expectedWords && !IsZero(words) && modulus.IsBelowModulus(words);

    private static bool IsZero(ReadOnlySpan<uint> words)
    {
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
