namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides hardware-native fast approximation math functions for GPU kernels.
/// Maps to CUDA fast math intrinsics (e.g. <c>__expf</c>, <c>__logf</c>) bypassing standard IEEE-754 precision constraints for maximum throughput.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
#pragma warning disable IDE0051 // Justification: Public API exposed for library consumers
public static class FastMath
{
    /// <summary>
    /// Computes the fast base-e exponential of the specified single-precision floating-point value.
    /// </summary>
    /// <param name="x">The single-precision floating-point exponent.</param>
    /// <returns>The value <c>e^x</c> calculated using fast hardware approximation.</returns>
    /// <remarks>
    /// <para><b>Execution Target:</b> Usable in both CPU fallback and GPU device kernels.</para>
    /// <para><b>Error Bound:</b> On the GPU (CUDA <c>__expf</c>), maximum error is within 2 + floor(|1.173 * x|) ULP. The CPU fallback returns <see cref="MathF.Exp(float)"/>, which is correctly rounded (within 1 ULP).</para>
    /// </remarks>
    public static float Exp(float x) => MathF.Exp(x);

    /// <summary>
    /// Computes the fast natural (base-e) logarithm of the specified single-precision floating-point value.
    /// </summary>
    /// <param name="x">The single-precision floating-point number whose logarithm is to be computed.</param>
    /// <returns>The natural logarithm of <paramref name="x"/> calculated using fast hardware approximation.</returns>
    /// <remarks>
    /// <para><b>Execution Target:</b> Usable in both CPU fallback and GPU device kernels.</para>
    /// <para><b>Error Bound:</b> On the GPU (CUDA <c>__logf</c>), maximum absolute error is 2^-21.41 (approx. 3.58e-7) for <paramref name="x"/> in [0.5, 2.0], and within 3 ULP otherwise. The CPU fallback returns <see cref="MathF.Log(float)"/>, which is correctly rounded (within 1 ULP).</para>
    /// </remarks>
    public static float Log(float x) => MathF.Log(x);

    /// <summary>
    /// Computes the fast square root of the specified single-precision floating-point value.
    /// </summary>
    /// <param name="x">The single-precision floating-point number whose square root is to be computed.</param>
    /// <returns>The square root of <paramref name="x"/> calculated using fast hardware approximation.</returns>
    /// <remarks>
    /// <para><b>Execution Target:</b> Usable in both CPU fallback and GPU device kernels.</para>
    /// </remarks>
    public static float Sqrt(float x) => MathF.Sqrt(x);

    /// <summary>
    /// Computes the absolute value of the specified single-precision floating-point value.
    /// </summary>
    /// <param name="x">The single-precision floating-point value.</param>
    /// <returns>The absolute value of <paramref name="x"/>.</returns>
    /// <remarks>
    /// <para><b>Execution Target:</b> Usable in both CPU fallback and GPU device kernels.</para>
    /// </remarks>
    public static float Abs(float x) => MathF.Abs(x);
}

