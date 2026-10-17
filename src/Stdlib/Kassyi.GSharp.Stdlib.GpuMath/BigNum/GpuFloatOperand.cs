namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents an arbitrary-precision floating-point operand consisting of a device memory significand handle, binary exponent, and sign flag.
/// </summary>
/// <param name="Significand">The memory handle referencing the raw significand limb buffer.</param>
/// <param name="Exponent">The base-2 exponent of the operand.</param>
/// <param name="IsNegative"><see langword="true"/> if the operand is negative; otherwise, <see langword="false"/>.</param>
public readonly record struct GpuFloatOperand(
    GpuBigIntMemoryHandle Significand,
    int Exponent,
    bool IsNegative = false)
{
    /// <summary>
    /// Creates a <see cref="GpuFloatOperand"/> from any <see cref="IGpuBigNumber{TSelf}"/> value.
    /// </summary>
    /// <typeparam name="T">The GPU big number type.</typeparam>
    /// <param name="number">The source GPU big number.</param>
    /// <param name="negate">If <see langword="true"/>, inverts the sign of the operand.</param>
    /// <returns>A <see cref="GpuFloatOperand"/> representing the number.</returns>
    public static GpuFloatOperand From<T>(T number, bool negate = false)
        where T : struct, IGpuBigNumber<T>
        => new(number.Magnitude.Handle, number.Exponent, negate ? !number.IsNegative : number.IsNegative);
}
