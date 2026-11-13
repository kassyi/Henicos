namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// A point of a squaring chain that passed a Gerbicz-Li check: the chain can resume from it.
/// </summary>
/// <param name="Iteration">The number of squarings <c>k</c>, a multiple of the block size.</param>
/// <param name="State">The canonical <c>u_k</c>.</param>
/// <param name="Check">The canonical Gerbicz-Li product <c>d</c>, which includes <c>u_k</c>.</param>
/// <param name="Snapshot">The canonical snapshot value when <c>k</c> is past the snapshot point; otherwise empty.</param>
/// <param name="Final">The canonical value at the end of the chain (<c>u_K</c>) when <c>k</c> is past <c>K</c>; otherwise
/// empty. A point at <c>K' &gt; K</c> must carry it: the run resumed from there squares nothing and reads it.</param>
/// <param name="ChecksPassed">The checks passed up to this point.</param>
/// <param name="Rollbacks">The rollbacks up to this point.</param>
internal sealed record ChainPoint(
    long Iteration,
    uint[] State,
    uint[] Check,
    uint[] Snapshot,
    uint[] Final,
    int ChecksPassed,
    int Rollbacks);
