namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Context parameters for NTT-related operations in Fermat sequences.
/// </summary>
public struct Ntt
{
    /// <summary>The power-of-two transformation length of the transform.</summary>
    public int TransformLength;
    /// <summary>The scratch buffer context for intermediate convolutions.</summary>
    public BufferContext Buffers;
    /// <summary>The precomputed roots of unity and twiddle factor context.</summary>
    public TwiddleFactorContext TwiddleFactors;
}
