namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates intermediate multi-precision polynomial and iteration scratch buffers for Newton-Raphson and Barrett steps.
/// </summary>
public struct IterationScratchContext
{
    /// <summary>Scratch buffer for squared variable $X^2$.</summary>
    public uint[]? XSquared;
    /// <summary>Scratch buffer for scaled squared variable $A \times X^2$.</summary>
    public uint[]? AxSquared;
    /// <summary>Scratch buffer for current iteration product.</summary>
    public uint[]? CurrentProduct;
    /// <summary>Scratch buffer for difference vector.</summary>
    public uint[] Diff;
    /// <summary>Scratch buffer for unshifted next iteration result.</summary>
    public uint[]? NextXUnshifted;
    /// <summary>Scratch buffer for bit-shifted next iteration result.</summary>
    public uint[]? NextXShifted;
    /// <summary>Scratch buffer for next iteration value.</summary>
    public uint[]? NextIter;
    /// <summary>Expanded modulus for Barrett reduction.</summary>
    public uint[]? ModulusExpanded;
}