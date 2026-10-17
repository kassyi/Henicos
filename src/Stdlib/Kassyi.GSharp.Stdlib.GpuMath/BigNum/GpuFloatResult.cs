namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents the resulting binary exponent and sign flag produced by an arbitrary-precision floating-point operation.
/// </summary>
/// <param name="Exponent">The resulting base-2 exponent.</param>
/// <param name="IsNegative"><see langword="true"/> if the resulting value is negative; otherwise, <see langword="false"/>.</param>
public readonly record struct GpuFloatResult(
    int Exponent,
    bool IsNegative);
