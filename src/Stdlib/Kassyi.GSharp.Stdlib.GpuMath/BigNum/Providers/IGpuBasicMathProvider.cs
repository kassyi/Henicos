namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines elementary arbitrary-precision arithmetic, bitwise operations, and relational predicates executed on GPU device kernels.
/// </summary>
public interface IGpuBasicMathProvider
{
    /// <summary>
    /// Adds two arbitrary-precision unsigned integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing kernel parameters.</param>
    /// <param name="leftOperand">The left unsigned integer operand.</param>
    /// <param name="rightOperand">The right unsigned integer operand.</param>
    /// <param name="resultBuffer">The destination buffer where the sum will be written.</param>
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Subtracts the right operand from the left operand on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing kernel parameters.</param>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The subtrahend operand.</param>
    /// <param name="resultBuffer">The destination buffer where the difference will be written.</param>
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Adds a 32-bit unsigned integer scalar to an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The big integer operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar.</param>
    /// <param name="resultBuffer">The destination buffer, at least as wide as <paramref name="leftOperand"/>; the
    /// result width rules and domain errors are those of
    /// <see cref="GpuBigIntMath.Add(GpuBigUIntView, uint, GpuBigUIntView)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than
    /// <paramref name="leftOperand"/>.</exception>
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Subtracts a 32-bit unsigned integer scalar from an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The big integer operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar.</param>
    /// <param name="resultBuffer">The destination buffer, at least as wide as <paramref name="leftOperand"/>; the
    /// result width rules and domain errors are those of
    /// <see cref="GpuBigIntMath.Sub(GpuBigUIntView, uint, GpuBigUIntView)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than
    /// <paramref name="leftOperand"/>.</exception>
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise AND of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise OR of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise XOR of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise NOT (one's complement) of an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The source operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Shifts an arbitrary-precision integer left by the specified bit count on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift left.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);

    /// <summary>
    /// Shifts an arbitrary-precision integer right by the specified bit count on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift right.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the square of a large integer in-place or into the specified pointer.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="devicePointer">The pointer to the device memory containing the limbs.</param>
    /// <param name="logicalWords">The number of active limbs.</param>
    /// <returns><see langword="true"/> if squaring succeeded; otherwise, <see langword="false"/>.</returns>
    bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);

    /// <summary>
    /// Evaluates whether the given arbitrary-precision integer is equal to zero on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand to evaluate.</param>
    /// <returns><see langword="true"/> if the value is zero; otherwise, <see langword="false"/>.</returns>
    bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Evaluates whether the given arbitrary-precision integer is equal to one on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand to evaluate.</param>
    /// <returns><see langword="true"/> if the value is one; otherwise, <see langword="false"/>.</returns>
    bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Computes the active bit length of the given arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand.</param>
    /// <returns>The number of significant bits required to represent the integer.</returns>
    int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Reads the bit length and the leading 64 bits of an arbitrary-precision integer with one wait for the device.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="value">The integer operand.</param>
    /// <param name="bitLength">When this method returns, the number of significant bits of <paramref name="value"/>
    /// (0 for zero).</param>
    /// <param name="leadingBits">When this method returns, the top 64 bits of <paramref name="value"/>, its top set
    /// bit at bit 63 (missing low bits are 0; 0 for zero).</param>
    void ReadLeadingBits(GpuWorkspace workspace, GpuBigUInt value, out int bitLength, out ulong leadingBits);

    /// <summary>
    /// Compares two arbitrary-precision unsigned integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <returns>A signed integer indicating the relative order (<c>-1</c> if left &lt; right, <c>0</c> if equal, <c>1</c> if left &gt; right).</returns>
    int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);

    /// <summary>
    /// Divides an arbitrary-precision unsigned integer by a 32-bit unsigned scalar on the GPU and returns the remainder.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="dividend">The dividend big integer operand.</param>
    /// <param name="divisor">The 32-bit unsigned scalar divisor.</param>
    /// <param name="quotientBuffer">The destination buffer where the quotient will be written.</param>
    /// <returns>The 32-bit unsigned scalar remainder (<c>dividend % divisor</c>).</returns>
    uint DivideByUInt32(GpuWorkspace workspace, GpuBigUInt dividend, uint divisor, GpuBigUInt quotientBuffer);
}
