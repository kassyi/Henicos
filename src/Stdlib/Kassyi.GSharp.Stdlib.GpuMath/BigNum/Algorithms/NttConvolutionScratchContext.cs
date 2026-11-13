namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates dual-prime NTT convolution and CRT carry-resolution scratch buffers.
/// </summary>
public struct NttConvolutionScratchContext
{
    /// <summary>First NTT scratch buffer for operand A.</summary>
    public uint[] NttBuf1A;
    /// <summary>Second NTT scratch buffer for operand A.</summary>
    public uint[] NttBuf2A;
    /// <summary>First NTT scratch buffer for operand B.</summary>
    public uint[] NttBuf1B;
    /// <summary>Second NTT scratch buffer for operand B.</summary>
    public uint[] NttBuf2B;
    /// <summary>General temporary buffer.</summary>
    public uint[] TmpBuf;
    /// <summary>Uncarried lower-half word accumulator.</summary>
    public uint[] UncarriedLow;
    /// <summary>Uncarried upper-half word accumulator.</summary>
    public uint[] UncarriedHigh;
    /// <summary>Block-level carry propagation array.</summary>
    public uint[] BlockCarries;
}