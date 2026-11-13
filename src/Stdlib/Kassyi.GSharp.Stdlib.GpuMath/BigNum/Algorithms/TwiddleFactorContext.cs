namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates precomputed forward and inverse NTT twiddle factors for multi-prime moduli.
/// </summary>
public struct TwiddleFactorContext
{
    /// <summary>Forward NTT twiddle factors for prime 1.</summary>
    [GpuReadOnly] public uint[] Forward1;
    /// <summary>Forward NTT twiddle factors for prime 2.</summary>
    [GpuReadOnly] public uint[] Forward2;
    /// <summary>Inverse NTT twiddle factors for prime 1.</summary>
    [GpuReadOnly] public uint[] Inverse1;
    /// <summary>Inverse NTT twiddle factors for prime 2.</summary>
    [GpuReadOnly] public uint[] Inverse2;
}
