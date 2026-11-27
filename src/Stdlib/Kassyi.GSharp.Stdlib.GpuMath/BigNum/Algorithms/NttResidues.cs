namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Holds the prime-1 and prime-2 NTT residue buffers of one operand in transform order.
/// </summary>
public struct NttResidues
{
    /// <summary>Residue buffer for NTT prime 1, in transform order.</summary>
    public uint[]? Buffer1;

    /// <summary>Residue buffer for NTT prime 2, in transform order.</summary>
    public uint[]? Buffer2;
}
