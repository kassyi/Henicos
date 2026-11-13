namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates multi-precision word sizes, bit widths, and iteration counts for big integer algorithms.
/// </summary>
public struct DimensionContext
{
    /// <summary>The base number of 32-bit words in the operands.</summary>
    public int NumberOfWords;
    /// <summary>The extended word count used during intermediate multiplication.</summary>
    public int NumberOfWordsExtended;
    /// <summary>The exponent value in power/modular operations.</summary>
    public int Exponent;
    /// <summary>The bit offset <c>2E + 1</c> of a fold modulo <c>2^(2E+1) - 2^E + 11</c> (no kernel reads it at present).</summary>
    public int BitOffset2E1;
    /// <summary>The margin word size for precision safety.</summary>
    public int MarginWords;
    /// <summary>The actual bit length of the values.</summary>
    public int ActualBits;
    /// <summary>The margin bits added to prevent precision loss.</summary>
    public int MarginBits;
    /// <summary>The total number of iterations to perform.</summary>
    public int Iterations;
    /// <summary>The target precision in bits.</summary>
    public int TargetBits;
    /// <summary>The total number of bits.</summary>
    public int NumBits;
}