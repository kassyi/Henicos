namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates dual-prime NTT convolution and CRT carry-resolution scratch buffers.
/// </summary>
public struct NttConvolutionScratchContext
{
    /// <summary>
    /// Working residues for the freshly transformed operand, pointwise product, and inverse transform.
    /// </summary>
    public NttResidues Product;
    /// <summary>First kept operand transform reused across products (for example <c>x̂</c> or <c>x̂Sq</c>).</summary>
    public NttResidues Kept1;
    /// <summary>
    /// Second kept operand transform reused across iterations (for example <c>â</c>, <c>b̂</c>, or <c>R̂</c>).
    /// </summary>
    public NttResidues Kept2;
    /// <summary>
    /// Third kept operand transform used only by ModPow (<c>Q̂</c>); empty for InverseSqrt and Reciprocal.
    /// </summary>
    public NttResidues Kept3;
    /// <summary>General temporary buffer.</summary>
    public uint[] TmpBuf;
    /// <summary>Uncarried lower-half word accumulator.</summary>
    public uint[] UncarriedLow;
    /// <summary>Uncarried upper-half word accumulator.</summary>
    public uint[] UncarriedHigh;
    /// <summary>Block-level carry propagation array.</summary>
    public uint[] BlockCarries;
}
