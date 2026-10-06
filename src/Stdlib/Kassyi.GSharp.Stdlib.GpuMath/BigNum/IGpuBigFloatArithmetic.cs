namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Defines the contract for arbitrary-precision floating-point arithmetic operations executed across GPU and CPU backends.
/// </summary>
/// <remarks>
/// <para>
/// This interface abstracts low-level mantissa and exponent transformations for <see cref="BigNum.GpuBigFloat"/> and <see cref="BigNum.GpuBigUFloat"/> representations,
/// operating directly on raw memory handles (<see cref="GpuBigIntMemoryHandle"/>) via an execution context.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Implementations must execute without performing dynamic heap allocations on the managed runtime or GPU driver paths during calculation.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Implementations are generally stateful or tied to specific CUDA streams / memory contexts, and instances should not be shared across concurrent threads without synchronization.
/// </para>
/// </remarks>
public interface IGpuBigFloatArithmetic
{
    /// <summary>
    /// Normalizes an arbitrary-precision floating-point value such that the most significant limb of the significand is non-zero.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The memory handle referencing the raw significand limb buffer to normalize.</param>
    /// <param name="valueExp">The base-2 or limb exponent of the input value.</param>
    /// <param name="valueSign"><see langword="true"/> if the value is negative; otherwise, <see langword="false"/>.</param>
    /// <param name="resultSig">The memory handle where the normalized significand limbs will be written.</param>
    /// <param name="resultExp">When this method returns, contains the updated exponent adjusted for normalization shift.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign flag.</param>
    void Normalize(ref GpuBigIntExecutionContext context,
                   GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                   GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Adds two arbitrary-precision floating-point operands.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="leftSig">The significand handle of the left operand.</param>
    /// <param name="leftExp">The exponent of the left operand.</param>
    /// <param name="leftSign">The sign of the left operand.</param>
    /// <param name="rightSig">The significand handle of the right operand.</param>
    /// <param name="rightExp">The exponent of the right operand.</param>
    /// <param name="rightSign">The sign of the right operand.</param>
    /// <param name="resultSig">The memory handle where the resulting significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Add(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
             GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Multiplies two arbitrary-precision floating-point operands.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="leftSig">The significand handle of the left operand.</param>
    /// <param name="leftExp">The exponent of the left operand.</param>
    /// <param name="leftSign">The sign of the left operand.</param>
    /// <param name="rightSig">The significand handle of the right operand.</param>
    /// <param name="rightExp">The exponent of the right operand.</param>
    /// <param name="rightSign">The sign of the right operand.</param>
    /// <param name="resultSig">The memory handle where the product significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Multiply(ref GpuBigIntExecutionContext context,
                  GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Divides one arbitrary-precision floating-point operand by another.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="leftSig">The significand handle of the dividend.</param>
    /// <param name="leftExp">The exponent of the dividend.</param>
    /// <param name="leftSign">The sign of the dividend.</param>
    /// <param name="rightSig">The significand handle of the divisor.</param>
    /// <param name="rightExp">The exponent of the divisor.</param>
    /// <param name="rightSign">The sign of the divisor.</param>
    /// <param name="resultSig">The memory handle where the quotient significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Divide(ref GpuBigIntExecutionContext context,
                GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Computes the reciprocal (<c>1 / x</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the input value.</param>
    /// <param name="valueExp">The exponent of the input value.</param>
    /// <param name="valueSign">The sign of the input value.</param>
    /// <param name="resultSig">The memory handle where the reciprocal significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Reciprocal(ref GpuBigIntExecutionContext context,
                    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Computes the square root (<c>sqrt(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the input radicand.</param>
    /// <param name="valueExp">The exponent of the input radicand.</param>
    /// <param name="valueSign">The sign of the input radicand.</param>
    /// <param name="resultSig">The memory handle where the square root significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Sqrt(ref GpuBigIntExecutionContext context,
              GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
              GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Computes the reciprocal square root (<c>1 / sqrt(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the input value.</param>
    /// <param name="valueExp">The exponent of the input value.</param>
    /// <param name="valueSign">The sign of the input value.</param>
    /// <param name="resultSig">The memory handle where the reciprocal square root significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Rsqrt(ref GpuBigIntExecutionContext context,
               GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Computes the natural exponential (<c>e^x</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the exponent operand.</param>
    /// <param name="valueExp">The exponent of the exponent operand.</param>
    /// <param name="valueSign">The sign of the exponent operand.</param>
    /// <param name="resultSig">The memory handle where the computed result significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Exp(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Computes the natural logarithm (<c>ln(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the input argument.</param>
    /// <param name="valueExp">The exponent of the input argument.</param>
    /// <param name="valueSign">The sign of the input argument.</param>
    /// <param name="resultSig">The memory handle where the logarithm significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Log(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Raises an arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent (<c>base^exp</c>).
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="baseSig">The significand handle of the base.</param>
    /// <param name="baseExp">The exponent of the base.</param>
    /// <param name="baseSign">The sign of the base.</param>
    /// <param name="expSig">The significand handle of the power exponent.</param>
    /// <param name="exponentExp">The exponent of the power exponent.</param>
    /// <param name="exponentSign">The sign of the power exponent.</param>
    /// <param name="resultSig">The memory handle where the computed power significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    void Pow(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle baseSig, int baseExp, bool baseSign,
             GpuBigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    /// <summary>
    /// Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="valueSig">The significand handle of the dividend.</param>
    /// <param name="valueExp">The exponent of the dividend.</param>
    /// <param name="valueSign">The sign of the dividend.</param>
    /// <param name="divisor">The 32-bit unsigned scalar divisor.</param>
    /// <param name="resultSig">The memory handle where the quotient significand will be stored.</param>
    /// <param name="resultExp">When this method returns, contains the resulting exponent.</param>
    /// <param name="resultSign">When this method returns, contains the resulting sign.</param>
    /// <param name="remainder">When this method returns, contains the scalar remainder of the operation.</param>
    void DivideByUInt32(ref GpuBigIntExecutionContext context,
                        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                        uint divisor,
                        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder);
}

