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
    /// records <c>2 * 2^k</c> leaves and <c>2^k</c> workspace slots, totaling <c>3 * 2^k</c> slots (each
    /// <c>ceil((2E + 1) / 32)</c> 32-bit words, about 13.7 GB at <c>E = 71,428,571</c>, <c>k = 8</c>, held in RAM unless
    /// <see cref="ProofLeafFile"/> is set) and stores the finalized proof in <see cref="FermatAlgorithm.LastProof"/>.
    /// </summary>
    public int ProofPower { get; init; }

    /// <summary>
    /// Gets an optional file path where the <c>2 * 2^k</c> leaves and <c>2^k</c> workspace slots, totaling
    /// <c>3 * 2^k</c> slots (each <c>ceil((2E + 1) / 32)</c> 32-bit words, about 13.7 GB at <c>E = 71,428,571</c>,
    /// <c>k = 8</c>) are stored on disk instead of host RAM, keeping RAM usage bounded to <c>O(ResidueWords)</c> and
    /// persisting leaves across checkpoint restarts.
    /// Ignored when <see cref="ProofPower"/> is 0.
    /// </summary>
    public string? ProofLeafFile { get; init; }
}
