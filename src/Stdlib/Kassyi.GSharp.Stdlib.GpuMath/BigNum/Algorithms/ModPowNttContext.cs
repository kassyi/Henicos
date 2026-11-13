namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates context parameters for the NTT-Barrett Modular Exponentiation MegaKernel.
/// </summary>
public struct ModPowNttContext
{
    /// <summary>Core Newton-Raphson and NTT execution state.</summary>
    public NewtonRaphsonContext Core;
    /// <summary>Reciprocal modulus for Barrett reduction.</summary>
    public uint[] ReciprocalMod;
    /// <summary>Windowed exponent slice parameters.</summary>
    public ExponentSliceContext Exponent;
}
