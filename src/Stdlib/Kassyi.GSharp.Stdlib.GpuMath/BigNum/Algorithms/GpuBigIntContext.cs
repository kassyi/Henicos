namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions.
/// </summary>
public struct GpuBigIntContext
{
    /// <summary>The operand buffers containing input values and destination arrays.</summary>
    public OperandContext Operands;
    /// <summary>The total number of 32-bit words in the multi-precision integer.</summary>
    public int NumberOfWords;
}