namespace Henicos.Benchmarks;

/// <summary>
/// Selects the NTT-backed big-integer operation timed by <see cref="GpuBigIntNttProductBenchmark"/>.
/// </summary>
public enum NttProductKernel
{
    /// <summary>Cooperative mega-kernel inverse square root (<c>GpuBigIntDivRem.InverseSqrt</c>).</summary>
    InverseSqrt,

    /// <summary>Cooperative mega-kernel reciprocal (<c>GpuBigIntDivRem.Reciprocal</c>).</summary>
    Reciprocal,

    /// <summary>Cooperative mega-kernel modular exponentiation (<c>GpuBigIntDivRem.ModPow</c>).</summary>
    ModPow,

    /// <summary>Standalone 3-prime 35-bit NTT multiplication (<c>GpuBigIntMath.Mul(a, b, r)</c>).</summary>
    Multiply,

    /// <summary>Standalone 3-prime 35-bit NTT squaring (<c>GpuBigIntMath.Mul(a, a, r)</c>).</summary>
    Square,
}
