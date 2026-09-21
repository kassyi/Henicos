namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides hardware-native fast approximation math functions for GPU kernels.
/// Maps to CUDA fast math intrinsics (e.g. __expf, __logf) bypassing standard IEEE-754 precision constraints for maximum throughput.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
#pragma warning disable IDE0051 // Justification: Public API exposed for library consumers
public static class FastMath
{
    /// <summary>Computes the base-e exponential of the specified value using the fast approximation instruction (__expf).</summary>
    [CudaIntrinsic("__expf")]
    public static float Exp(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the natural logarithm of the specified value using the fast approximation instruction (__logf).</summary>
    [CudaIntrinsic("__logf")]
    public static float Log(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the square root of the specified value using the fast hardware instruction.</summary>
    [CudaIntrinsic("__fsqrt_rn")]
    public static float Sqrt(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the absolute value of the specified value.</summary>
    [CudaIntrinsic("fabsf")]
    public static float Abs(float x) => GpuOnlyException.Throw<float>();
}
