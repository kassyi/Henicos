using System.Diagnostics.Contracts;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// The root contract for all GPU big number types (both fixed-width stack/register structs and arbitrary-precision device-backed structs).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mathematical Model:</b> Every GPU number represents a value $(-1)^{\text{IsNegative}} \times \text{Magnitude} \times 2^{\text{Exponent}}$,
/// where unsigned types are simply numbers with <c>IsNegative == false</c> (<c>CanBeNegative == false</c>) and integers are numbers with <c>Exponent == 0</c>.
/// </para>
/// </remarks>
public interface IGpuBigNumberBase
{
    /// <summary>
    /// Gets a value indicating whether this number is negative. Always <see langword="false"/> for unsigned types.
    /// </summary>
    bool IsNegative { get; }

    /// <summary>
    /// Gets a value indicating whether this number type can represent negative values (<see langword="true"/> for signed types; <see langword="false"/> for unsigned types).
    /// </summary>
    bool CanBeNegative { get; }

    /// <summary>
    /// Determines whether the magnitude of this number is zero.
    /// </summary>
    /// <returns><see langword="true"/> if the number equals zero; otherwise, <see langword="false"/>.</returns>
    [Pure]
    bool IsZero();

    /// <summary>
    /// Calculates the minimum number of bits required to represent the magnitude of this number without leading zeros, or 0 if zero.
    /// </summary>
    /// <returns>The bit length of the magnitude, or 0 if zero.</returns>
    [Pure]
    int GetBitLength();
}
