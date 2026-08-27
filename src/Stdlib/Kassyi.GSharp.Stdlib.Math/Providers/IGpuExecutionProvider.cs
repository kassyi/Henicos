namespace Kassyi.GSharp.Stdlib.Math.Providers;

/// <summary>
/// Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory.
/// </summary>
public interface IGpuExecutionProvider
{
    /// <summary>Allocates unmanaged global device memory for the specified word count.</summary>
    /// <param name="numWords">The number of 32-bit words to allocate.</param>
    /// <returns>A pointer to the allocated device memory.</returns>
    IntPtr AllocateMemory(int numWords);

    /// <summary>Frees the allocated unmanaged global device memory.</summary>
    /// <param name="devicePointer">The pointer to the device memory to free.</param>
    void FreeMemory(IntPtr devicePointer);

    /// <summary>Copies limb data from host managed memory to GPU device memory.</summary>
    /// <param name="devicePointer">The destination pointer on the device.</param>
    /// <param name="hostData">The source data array on the host.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords);

    /// <summary>Copies limb data from GPU device memory to host managed memory.</summary>
    /// <param name="hostData">The destination array on the host.</param>
    /// <param name="devicePointer">The source pointer on the device.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords);

    /// <summary>Copies limb data between two GPU device memory locations.</summary>
    /// <param name="destPointer">The destination pointer on the device.</param>
    /// <param name="srcPointer">The source pointer on the device.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords);

    /// <summary>Computes addition on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes subtraction on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes multiplication on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes scalar addition on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes scalar subtraction on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes scalar multiplication on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes bitwise AND on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes bitwise OR on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes bitwise XOR on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes bitwise NOT on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer);

    /// <summary>Computes left bit shift on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer);

    /// <summary>Computes right bit shift on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer);

    /// <summary>Computes in-place self squaring on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="devicePointer">The pointer to the device memory containing the operand.</param>
    /// <param name="logicalWords">The logical number of words of the operand.</param>
    /// <returns><c>true</c> if successful; otherwise, <c>false</c>.</returns>
    bool Square(GpuContext context, IntPtr devicePointer, int logicalWords);

    /// <summary>Computes Greatest Common Divisor on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    /// <summary>Checks whether operand equals zero on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to check.</param>
    /// <returns><c>true</c> if the operand is zero; otherwise, <c>false</c>.</returns>
    bool IsZero(GpuContext context, GpuBigInteger leftOperand);

    /// <summary>Checks whether operand equals one on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to check.</param>
    /// <returns><c>true</c> if the operand is one; otherwise, <c>false</c>.</returns>
    bool IsOne(GpuContext context, GpuBigInteger leftOperand);

    /// <summary>Gets the bit length of the operand on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand.</param>
    /// <returns>The bit length.</returns>
    int GetBitLength(GpuContext context, GpuBigInteger leftOperand);

    /// <summary>Compares two operands on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A signed integer that indicates the relative values of the operands.</returns>
    int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand);
}


