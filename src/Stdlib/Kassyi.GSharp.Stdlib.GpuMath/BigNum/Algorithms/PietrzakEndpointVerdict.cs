namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>Holds the Fermat probable-prime congruence outcome computed on verified endpoints.</summary>
internal readonly record struct PietrzakEndpointVerdict(bool IsProbablePrime, ulong Residue64, ulong CheckResidue64);

