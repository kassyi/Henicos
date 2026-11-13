namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates the 2D Six-Step NTT grid dimensions ($M = N_1 \times N_2$).
/// </summary>
public struct NttGridContext
{
    /// <summary>Batch/total transform dimension parameter M.</summary>
    public int M;
    /// <summary>First transform length dimension N1.</summary>
    public int N1;
    /// <summary>Second transform length dimension N2.</summary>
    public int N2;
}
