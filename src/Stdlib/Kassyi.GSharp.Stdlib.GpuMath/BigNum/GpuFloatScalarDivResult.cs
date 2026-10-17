namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents the resulting binary exponent, sign flag, and 32-bit scalar remainder produced by a scalar division operation.
/// </summary>
/// <param name="Exponent">The resulting base-2 exponent.</param>
/// <param name="IsNegative"><see langword="true"/> if the resulting quotient is negative; otherwise, <see langword="false"/>.</param>
/// <param name="Remainder">The 32-bit unsigned scalar remainder.</param>
public readonly record struct GpuFloatScalarDivResult(
    int Exponent,
    bool IsNegative,
    uint Remainder);
