namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>The progress of <see cref="FermatAlgorithm.RunAsync"/>, reported at every block boundary.</summary>
/// <param name="Iteration">The squarings done (the position in the chain).</param>
/// <param name="TotalIterations">The length of the chain, rounded up to a block boundary.</param>
/// <param name="ChecksPassed">The Gerbicz-Li checks passed.</param>
/// <param name="Rollbacks">The rollbacks after failed checks.</param>
public readonly record struct FermatPrpProgress(long Iteration, long TotalIterations, int ChecksPassed, int Rollbacks);
