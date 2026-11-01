namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>Pairs the start/left and end/right residues of a Pietrzak claim (<c>(X, Y)</c> or <c>(u_E, u_K)</c>).</summary>
internal readonly record struct PietrzakClaim(uint[] X, uint[] Y);

