namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates a windowed slice of an exponent buffer for chunked modular exponentiation.
/// </summary>
public struct ExponentSliceContext
{
    /// <summary>Exponent word buffer.</summary>
    public uint[] Words;
    /// <summary>Total number of words in the exponent.</summary>
    public int TotalWords;
    /// <summary>Starting word index for exponentiation.</summary>
    public int StartWord;
    /// <summary>Ending word index for exponentiation.</summary>
    public int EndWord;
}
