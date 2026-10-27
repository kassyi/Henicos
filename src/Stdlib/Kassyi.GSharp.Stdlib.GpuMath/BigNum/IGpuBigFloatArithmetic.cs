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
    /// <param name="resultSig">The memory handle where the computed result significand will be stored. Its word count
    /// <c>n</c> sets the precision of the result, <c>p = 32·n</c> bits.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag (always positive).</returns>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>: an overflow
    /// for a large positive argument, an underflow (named in the message) for a large negative one. Every argument with
    /// <c>|x| ≥ 2^32</c> is out of range; the limits are about <c>±1.4885·10^9</c>.</exception>
    /// <remarks>
    /// <para>
    /// The significand fills <paramref name="resultSig"/> with its top bit set; <c>e^0 = 1</c> is
    /// <c>2^(p−1)·2^(1−p)</c>. The result is the exact value rounded toward zero to <c>p</c> bits, like
    /// <see cref="Add"/>, <see cref="Multiply"/> and <see cref="Divide"/>, for every argument in range, whatever its size
    /// or sign. The GPU and the CPU provider return the same bits.
    /// </para>
    /// <para>
    /// The argument is reduced to <c>x = k·ln 2 + r</c>, <c>r ∈ (0.69, 1.39)</c>, for either sign (no reciprocal), and
    /// <c>e^r</c> is evaluated as <c>(e^(r/2^s))^(2^s)</c> with a number of terms derived from the precision;
    /// <see cref="GpuWorkspace.Transcendental"/> can fix <c>s</c>.
    /// An approximation with guard bits is accepted only when every value within its error bound truncates to
    /// the same <c>p</c> bits; otherwise it is recomputed with more guard bits. That is rare for random arguments;
    /// arguments such as <c>±2^-c</c> (<c>c</c> above about <c>p/2</c>), whose exponential lies very close to a
    /// <c>p</c>-bit value, take two attempts or more.
    /// </para>
    /// </remarks>
    GpuFloatResult Exp(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Computes the natural logarithm (<c>ln(x)</c>) of an arbitrary-precision floating-point value.
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="value">The input argument floating-point operand; positive.</param>
    /// <param name="resultSig">The memory handle where the logarithm significand will be stored. Its word count
    /// <c>n</c> sets the precision of the result, <c>p = 32·n</c> bits.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="value"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>.</exception>
    /// <remarks>
    /// <para>
    /// The significand fills <paramref name="resultSig"/> with its top bit set, or is zero for <c>ln 1 = 0</c>.
    /// The result is the exact logarithm rounded toward zero to <c>p</c> bits, like <see cref="Add"/>,
    /// <see cref="Multiply"/> and <see cref="Divide"/>, for every positive argument, whatever its exponent and however
    /// close it is to 1. The GPU and the CPU provider return the same bits.
    /// </para>
    /// <para>
    /// The argument is reduced to <c>x = m·2^e</c>, <c>m ∈ [3/4, 3/2)</c>, and <c>ln x = e·ln 2 + 2·atanh((m − 1) / (m + 1))</c>
    /// is evaluated with a number of terms derived from the precision.
    /// An approximation with guard bits is accepted only when every value within its error bound truncates to
    /// the same <c>p</c> bits; otherwise it is recomputed with more guard bits. That is rare for random arguments;
    /// arguments such as <c>1 ± 2^-c</c> (<c>c</c> above about <c>p/2</c>), whose logarithm lies very close to a
    /// <c>p</c>-bit value, take two attempts or more.
    /// </para>
    /// </remarks>
    GpuFloatResult Log(
        ref GpuBigIntExecutionContext context,
        in GpuFloatOperand value,
        GpuBigIntMemoryHandle resultSig);

    /// <summary>
    /// Raises a positive arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent
    /// (<c>x^y</c>).
    /// </summary>
    /// <param name="context">The execution context providing memory workspace and CUDA stream dispatch.</param>
    /// <param name="baseValue">The base <c>x</c>; positive.</param>
    /// <param name="exponentValue">The exponent <c>y</c>.</param>
    /// <param name="resultSig">The memory handle where the computed power significand will be stored. Its word count
    /// <c>n</c> sets the precision of the result, <c>p = 32·n</c> bits.</param>
    /// <returns>A <see cref="GpuFloatResult"/> containing the resulting exponent and sign flag (always positive).</returns>
    /// <exception cref="InvalidOperationException"><paramref name="baseValue"/> is zero or negative, whatever the
    /// exponent (also for <c>y = 0</c>).</exception>
    /// <exception cref="OverflowException">The exponent of the result does not fit in <see cref="int"/>: "Pow overflow"
    /// for a result above 1, "Pow underflow" for one below 1.</exception>
    /// <remarks>
    /// <para>
    /// The significand fills <paramref name="resultSig"/> with its top bit set. The result is the exact power rounded
    /// toward zero to <c>p</c> bits, like <see cref="Exp"/> and <see cref="Log"/>, for every positive base; the GPU and
    /// the CPU provider return the same bits.
    /// </para>
    /// <para>
    /// A power that is exactly a <c>p</c>-bit value is found first and returned as it is: <c>x^0 = 1</c>,
    /// <c>1^y = 1</c>, <c>2^3 = 8</c>, <c>4^0.5 = 2</c>, <c>(9/4)^0.5 = 3/2</c>, <c>2^-3 = 1/8</c>, and every other
    /// dyadic power of at most <c>p</c> significant bits, for an integer <c>y</c> and for <c>y = n / 2^k</c> (the check
    /// of MPFR's <c>mpfr_pow_is_exact</c>). Every other power is computed as <c>e^(y·L)</c> with <c>L ≈ ln x</c> to the
    /// Exp precision plus 64 bits and <c>y·L</c> formed exactly; the rounding check covers the Exp bound and the error
    /// that <c>L</c> carries into <c>y·L</c>, and retries as for <see cref="Exp"/>. It always ends, because such a power
    /// is never a <c>p</c>-bit value; powers next to one, such as <c>(2 + 2^-200)^3</c> at <c>p = 128</c>, take two
    /// attempts or more.
    /// </para>
    /// </remarks>
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
