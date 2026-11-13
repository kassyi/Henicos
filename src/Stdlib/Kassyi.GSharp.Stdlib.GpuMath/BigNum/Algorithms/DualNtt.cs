namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Context parameters for dual-pass NTT GPU execution.
/// </summary>
public struct DualNtt
{
    /// <summary>First NTT transformation buffer.</summary>
    public uint[] Buffer1;
    /// <summary>Second NTT transformation buffer.</summary>
    public uint[] Buffer2;
    /// <summary>General temporary scratch buffer.</summary>
    public uint[] TemporaryBuffer;
    /// <summary>Twiddle factor table 1.</summary>
    [GpuReadOnly] public uint[] Twiddle1;
    /// <summary>Twiddle factor table 2.</summary>
    [GpuReadOnly] public uint[] Twiddle2;
    /// <summary>2D NTT grid dimensions ($M, N_1, N_2$).</summary>
    public NttGridContext Grid;
}
