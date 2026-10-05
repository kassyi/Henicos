namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Specifies the IEEE 754 compliant rounding mode for arbitrary-precision GPU floating-point operations.
/// </summary>
/// <remarks>
/// <para>These rounding modes dictate the behavior of arbitrary-precision floating-point arithmetic (e.g. <see cref="GpuBigFloat"/> and <see cref="GpuBigUFloat"/>) when rounding significands to the target precision.</para>
/// <para><b>Execution Target:</b> Usable across both host and GPU execution environments.</para>
/// </remarks>
public enum GpuBigFloatRoundingMode
{
    /// <summary>
    /// Round to nearest, with ties rounding to the nearest even integer significand (IEEE 754 default).
    /// </summary>
    RoundToNearestEven = 0,

    /// <summary>
    /// Round toward zero (truncation).
    /// </summary>
    RoundTowardZero = 1,

    /// <summary>
    /// Round toward positive infinity.
    /// </summary>
    RoundTowardPositiveInfinity = 2,

    /// <summary>
    /// Round toward negative infinity.
    /// </summary>
    RoundTowardNegativeInfinity = 3
}

