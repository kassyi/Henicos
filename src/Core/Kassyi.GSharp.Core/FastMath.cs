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
    /// <summary>Computes the base-e exponential of the specified value. The emitted implementation and precision characteristics depend on the selected CPU or CUDA dialect.</summary>
    public static float Exp(float x) => MathF.Exp(x);

    /// <summary>Computes the natural logarithm of the specified value. The emitted implementation and precision characteristics depend on the selected CPU or CUDA dialect.</summary>
    public static float Log(float x) => MathF.Log(x);

    /// <summary>Computes the square root of the specified value. The emitted implementation and precision characteristics depend on the selected CPU or CUDA dialect.</summary>
    public static float Sqrt(float x) => MathF.Sqrt(x);

    /// <summary>Computes the absolute value of the specified value. The emitted implementation and precision characteristics depend on the selected CPU or CUDA dialect.</summary>
    public static float Abs(float x) => MathF.Abs(x);
}
