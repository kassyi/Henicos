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
    public static float Exp(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the natural logarithm of the specified value using the fast approximation instruction (__logf).</summary>
    public static float Log(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the square root of the specified value using the fast hardware instruction.</summary>
    public static float Sqrt(float x) => GpuOnlyException.Throw<float>();

    /// <summary>Computes the absolute value of the specified value.</summary>
    public static float Abs(float x) => GpuOnlyException.Throw<float>();
}
