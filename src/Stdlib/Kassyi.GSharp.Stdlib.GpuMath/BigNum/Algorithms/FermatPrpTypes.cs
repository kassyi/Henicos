namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Options of <see cref="FermatAlgorithm.Run"/>: the Gerbicz-Li block size, the check interval, and how often a check
/// may fail in a row.
/// </summary>
public sealed record FermatPrpOptions
{
    /// <summary>
    /// Gets the block size <c>B</c>: the Gerbicz-Li product is extended every <c>B</c> squarings, and a check costs
    /// <c>B</c> extra squarings. 0 (the default) chooses <c>clamp(floor(sqrt(2E + 1)), 2, 1000)</c>; otherwise it must be
    /// at most the chain length <c>2E + 1</c>.
    /// </summary>
    public int BlockSize { get; init; }

    /// <summary>
    /// Gets the number of blocks between two checks. 0 (the default) chooses <see cref="BlockSize"/> blocks, that is
    /// <c>B^2</c> squarings, as gpuowl does. The chain always ends with a check.
    /// </summary>
    public int CheckIntervalBlocks { get; init; }

    /// <summary>Gets the number of failed checks in a row that ends the run with an exception. The default is 3.</summary>
    public int MaxConsecutiveRollbacks { get; init; } = 3;

    /// <summary>
    /// Gets a callback that receives every point of the chain that passed a check, for example to save it to a file.
    /// It runs on the thread that runs the chain, between blocks.
    /// </summary>
    public Action<FermatPrpCheckpoint>? OnVerifiedCheckpoint { get; init; }

    /// <summary>
    /// Gets the Pietrzak halving tree depth <c>k</c> (<c>2^k</c> leaves per segment). 0 (the default) disables proof
    /// generation; a positive value (<c>1</c> to <see cref="FermatPrpProof.MaxProofPower"/>, with <c>2^k &lt;= E</c>)
    /// records intermediate chain leaves and stores the finalized proof in <see cref="FermatAlgorithm.LastProof"/>.
    /// </summary>
    public int ProofPower { get; init; }

    /// <summary>
    /// Gets an optional file path where intermediate Pietrzak leaves are stored during the run instead of host memory,
    /// keeping RAM usage bounded for multi-million-digit exponents and persisting leaves across checkpoint restarts.
    /// Ignored when <see cref="ProofPower"/> is 0.
    /// </summary>
    public string? ProofLeafFile { get; init; }
}

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

/// <summary>The progress of <see cref="FermatAlgorithm.RunAsync"/>, reported at every block boundary.</summary>
/// <param name="Iteration">The squarings done (the position in the chain).</param>
/// <param name="TotalIterations">The length of the chain, rounded up to a block boundary.</param>
/// <param name="ChecksPassed">The Gerbicz-Li checks passed.</param>
/// <param name="Rollbacks">The rollbacks after failed checks.</param>
public readonly record struct FermatPrpProgress(long Iteration, long TotalIterations, int ChecksPassed, int Rollbacks);

/// <summary>
/// A point of the chain of <see cref="FermatAlgorithm.Run"/> that passed a Gerbicz-Li check, from which a run can
/// resume (<see cref="FermatAlgorithm.Restore"/>). Immutable; the application serializes it.
/// </summary>
/// <remarks>
/// The residues are canonical (below <c>N</c>), <c>ceil((2E + 1) / 32)</c> words each. A point past <c>u_E</c> carries it
/// (<see cref="SnapshotE"/>), and a point past <c>u_K</c> (the last point, at <c>K'</c>) carries that too
/// (<see cref="FinalK"/>), so that a run resumed from any point gives the same verdict. The check covers the computation
/// up to this point, not the copy itself: a copy damaged in storage is detected by the checks after the restore, which
/// then fail every time.
/// </remarks>
public sealed class FermatPrpCheckpoint
{
    /// <summary>Initializes a checkpoint, for example from a saved file.</summary>
    /// <param name="e">The exponent <c>E</c>.</param>
    /// <param name="base">The base <c>a</c>.</param>
    /// <param name="blockSize">The Gerbicz-Li block size the chain ran with.</param>
    /// <param name="iteration">The squarings done, a multiple of <paramref name="blockSize"/>.</param>
    /// <param name="state">The canonical state <c>u_k</c>.</param>
    /// <param name="check">The canonical Gerbicz-Li product <c>d</c>.</param>
    /// <param name="snapshotE">The canonical <c>u_E</c> when <paramref name="iteration"/> &gt; <c>E</c>; otherwise empty.</param>
    /// <param name="finalK">The canonical <c>u_K</c> (<c>K = 2E + 1</c>) when <paramref name="iteration"/> &gt; <c>K</c>;
    /// otherwise empty.</param>
    /// <param name="checksPassed">The checks passed up to this point.</param>
    /// <param name="rollbacks">The rollbacks up to this point.</param>
    public FermatPrpCheckpoint(int e, uint @base, int blockSize, long iteration, ReadOnlyMemory<uint> state,
        ReadOnlyMemory<uint> check, ReadOnlyMemory<uint> snapshotE, ReadOnlyMemory<uint> finalK, int checksPassed,
        int rollbacks)
    {
        E = e;
        Base = @base;
        BlockSize = blockSize;
        Iteration = iteration;
        State = state.ToArray();
        Check = check.ToArray();
        SnapshotE = snapshotE.ToArray();
        FinalK = finalK.ToArray();
        ChecksPassed = checksPassed;
        Rollbacks = rollbacks;
    }

    /// <summary>Gets the exponent <c>E</c>.</summary>
    public int E { get; }

    /// <summary>Gets the base <c>a</c>.</summary>
    public uint Base { get; }

    /// <summary>Gets the Gerbicz-Li block size the chain ran with.</summary>
    public int BlockSize { get; }

    /// <summary>Gets the squarings done, a multiple of <see cref="BlockSize"/>.</summary>
    public long Iteration { get; }

    /// <summary>Gets the canonical state <c>u_k</c>.</summary>
    public ReadOnlyMemory<uint> State { get; }

    /// <summary>Gets the canonical Gerbicz-Li product <c>d</c>.</summary>
    public ReadOnlyMemory<uint> Check { get; }

    /// <summary>Gets the canonical <c>u_E</c>, or empty when <see cref="Iteration"/> &lt;= <c>E</c>.</summary>
    public ReadOnlyMemory<uint> SnapshotE { get; }

    /// <summary>Gets the canonical <c>u_K</c> (<c>K = 2E + 1</c>), or empty when <see cref="Iteration"/> &lt;= <c>K</c>.</summary>
    public ReadOnlyMemory<uint> FinalK { get; }

    /// <summary>Gets the checks passed up to this point.</summary>
    public int ChecksPassed { get; }

    /// <summary>Gets the rollbacks up to this point.</summary>
    public int Rollbacks { get; }
}
