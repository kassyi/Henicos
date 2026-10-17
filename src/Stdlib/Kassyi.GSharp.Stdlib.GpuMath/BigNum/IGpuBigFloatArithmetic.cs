namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Defines the contract for arbitrary-precision floating-point arithmetic operations executed across GPU and CPU backends.
/// </summary>
/// <remarks>
/// <para>
/// This interface abstracts low-level mantissa and exponent transformations for <see cref="BigNum.GpuBigFloat"/> and <see cref="BigNum.GpuBigUFloat"/> representations,
/// operating directly on structured operands (<see cref="GpuFloatOperand"/>) and raw memory handles (<see cref="GpuBigIntMemoryHandle"/>) via an execution context.
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
    /// Normalizes an arbitrary-precision floating-point value such that the most significant bit of the significand is aligned to the MSB of <paramref name="resultSig"/>.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input floating-point operand to normalize.</param>
    /// <param name="resultSig">The memory handle where the normalized significand limbs will be written.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the normalized exponent and sign flag.</returns>
    GpuFloatResult Normalize(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Adds two arbitrary-precision floating-point operands.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="left">The left floating-point operand.</param>
    /// <param name="right">The right floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the resulting significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Add(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Multiplies two arbitrary-precision floating-point operands.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="left">The left floating-point operand.</param>
    /// <param name="right">The right floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the product significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Multiply(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Divides one arbitrary-precision floating-point operand by another.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="left">The dividend floating-point operand.</param>
    /// <param name="right">The divisor floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the quotient significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Divide(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the reciprocal (<c>1 / x</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the reciprocal significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Reciprocal(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the square root (<c>sqrt(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input radicand floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the square root significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Sqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the reciprocal square root (<c>1 / sqrt(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the reciprocal square root significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Rsqrt(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the natural exponential (<c>e^x</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The exponent floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the computed result significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Exp(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the natural logarithm (<c>ln(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input argument floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the logarithm significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Log(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Raises an arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent (<c>base^exp</c>).
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="baseValue">The base floating-point operand.</param>
    /// <param name="exponentValue">The power exponent floating-point operand.</param>
    /// <param name="resultSig">The memory handle where the computed power significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    GpuFloatResult Pow(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand baseValue,
        in GpuFloatOperand exponentValue,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The dividend floating-point operand.</param>
    /// <param name="divisor">The 32-bit unsigned scalar divisor.</param>
    /// <param name="resultSig">The memory handle where the quotient significand will be stored.</param>
    /// <returns>A <see cref="GpuFloatScalarDivResult"/> containing the resulting exponent, sign flag, and 32-bit remainder.</returns>
    GpuFloatScalarDivResult DivideByUInt32(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        uint divisor,
        GpuBigIntMemoryHandle resultSig);
}
