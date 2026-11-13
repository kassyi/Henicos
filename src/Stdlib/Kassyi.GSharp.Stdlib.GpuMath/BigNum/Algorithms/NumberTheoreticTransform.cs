namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Execution arguments for NTT-related kernels.
/// </summary>
public struct NumberTheoreticTransform
{
    /// <summary>In-place transform buffer.</summary>
    public uint[] Buffer;
    /// <summary>Temporary scratch buffer.</summary>
    public uint[] TemporaryBuffer;

    /// <summary>Twiddle factor table.</summary>
    [GpuReadOnly]
    public uint[] Twiddle;

    /// <summary>2D NTT grid dimensions ($M, N_1, N_2$).</summary>
    public NttGridContext Grid;
    /// <summary>Prime modulus P.</summary>
    public uint P;
    /// <summary>Barrett reduction or multiplicative inverse parameter Pm.</summary>
    public ulong Pm;
    /// <summary>Indicates whether an inverse NTT is performed.</summary>
    public bool IsInverse;
    /// <summary>Indicates whether twiddle multiplication is applied before transform.</summary>
    public bool ApplyTwiddleBefore;
    /// <summary>Indicates whether twiddle multiplication is applied after transform.</summary>
    public bool ApplyTwiddleAfter;
    /// <summary>The maximum number of GPU blocks to dispatch.</summary>
    public int MaxBlocks;
    /// <summary>The current transform stage length L.</summary>
    public int CurrentL;
}
