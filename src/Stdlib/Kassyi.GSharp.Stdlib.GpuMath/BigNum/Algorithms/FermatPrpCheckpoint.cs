namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

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
