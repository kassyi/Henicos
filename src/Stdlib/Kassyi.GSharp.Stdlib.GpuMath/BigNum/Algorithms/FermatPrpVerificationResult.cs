namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>The outcome of <see cref="FermatAlgorithm.VerifyProof"/>.</summary>
public readonly record struct FermatPrpVerificationResult
{
    /// <summary>Gets whether the Pietrzak proof passed every domain guard and final squaring check.</summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets whether <see cref="IsValid"/> is <see langword="true"/> and <c>u_K * a^10 == u_E (mod N)</c>; always
    /// <see langword="false"/> when <see cref="IsValid"/> is <see langword="false"/>.
    /// </summary>
    public bool IsProbablePrime { get; init; }

    /// <summary>Gets the low 64 bits of <c>u_K * a^10 mod N</c> when <see cref="IsValid"/> is <see langword="true"/>; otherwise 0.</summary>
    public ulong Residue64 { get; init; }

    /// <summary>Gets the low 64 bits of <c>u_E mod N</c> when <see cref="IsValid"/> is <see langword="true"/>; otherwise 0.</summary>
    public ulong CheckResidue64 { get; init; }

    /// <summary>Gets the modular squarings performed during verification.</summary>
    public long Squarings { get; init; }

    /// <summary>Gets the wall-clock verification time.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Gets <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>; otherwise a diagnostic reason.</summary>
    public string? FailureReason { get; init; }

    internal static FermatPrpVerificationResult Valid(
        PietrzakEndpointVerdict verdict,
        long squarings,
        TimeSpan elapsed) =>
        new()
        {
            IsValid = true,
            IsProbablePrime = verdict.IsProbablePrime,
            Residue64 = verdict.Residue64,
            CheckResidue64 = verdict.CheckResidue64,
            Squarings = squarings,
            Elapsed = elapsed,
            FailureReason = null,
        };

    internal static FermatPrpVerificationResult Invalid(long squarings, TimeSpan elapsed, string reason) =>
        new()
        {
            IsValid = false,
            IsProbablePrime = false,
            Squarings = squarings,
            Elapsed = elapsed,
            FailureReason = reason,
        };
}

