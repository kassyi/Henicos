namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>The outcome of <see cref="SquaringChainDriver.RunAsync"/>.</summary>
/// <param name="EndPoint">The last verified point, at the end of the chain.</param>
/// <param name="Iterations">The squarings of the state actually run, including those repeated after rollbacks.</param>
internal readonly record struct ChainOutcome(ChainPoint EndPoint, long Iterations);
