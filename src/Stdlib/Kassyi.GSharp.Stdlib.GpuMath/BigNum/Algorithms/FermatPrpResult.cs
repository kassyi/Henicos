namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>The verdict and the residues of <see cref="FermatAlgorithm.Run"/>.</summary>
/// <param name="IsProbablePrime">
/// <see langword="true"/> when <c>a^(N-1) = 1 (mod N)</c>: <c>N</c> is a probable prime to base <c>a</c> (not a proof);
/// <see langword="false"/> when <c>N</c> is certainly composite.
/// </param>
/// <param name="SmallDivisor"><c>gcd(a, N)</c> when it is not 1. No squaring was done then: the residues, the
/// iterations, the checks and the rollbacks are 0.</param>
/// <param name="Residue64">The low 64 bits of <c>u_K * a^10 mod N</c> (<c>K = 2E + 1</c>).</param>
/// <param name="CheckResidue64">The low 64 bits of <c>u_E mod N</c>; equal to <paramref name="Residue64"/> when <c>N</c>
/// is a probable prime.</param>
/// <param name="Iterations">The squarings of the chain actually run, including those repeated after rollbacks.</param>
/// <param name="ChecksPassed">The Gerbicz-Li checks passed.</param>
/// <param name="Rollbacks">The rollbacks after failed checks.</param>
/// <param name="Elapsed">The time the run took.</param>
public readonly record struct FermatPrpResult(
    bool IsProbablePrime,
    uint? SmallDivisor,
    ulong Residue64,
    ulong CheckResidue64,
    long Iterations,
    int ChecksPassed,
    int Rollbacks,
    TimeSpan Elapsed);
