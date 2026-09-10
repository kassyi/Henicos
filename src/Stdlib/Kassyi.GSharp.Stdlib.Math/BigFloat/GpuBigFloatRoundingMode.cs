namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Specifies the IEEE 754 compliant rounding mode for arbitrary-precision GPU floating-point operations.
/// </summary>
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
