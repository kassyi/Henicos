namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Pairs an unsigned <see cref="GpuBigUInt"/> magnitude with its precomputed exact bit length.
/// </summary>
/// <param name="Magnitude">The unsigned magnitude buffer.</param>
/// <param name="BitLength">The exact bit length of <paramref name="Magnitude"/>.</param>
public readonly record struct BitSizedMagnitude(GpuBigUInt Magnitude, int BitLength);
