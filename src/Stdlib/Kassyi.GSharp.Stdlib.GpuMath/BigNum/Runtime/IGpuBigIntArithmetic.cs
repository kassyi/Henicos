namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

/// <summary>
/// Defines the low-level GPU device memory contract for arbitrary-precision integer arithmetic operations.
/// </summary>
/// <remarks>
/// <para>Implementations operate directly on <see cref="GpuBigIntMemoryHandle"/> descriptors without allocating host-side objects.</para>
/// <para><b>Thread Safety:</b> Implementations must be thread-safe for operations dispatched with non-overlapping memory handles and separate streams.</para>
/// </remarks>
public interface IGpuBigIntArithmetic
{
    /// <summary>Adds two big integers in GPU device memory (<c>result = left + right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the first addend.</param>
    /// <param name="right">The memory handle of the second addend.</param>
    /// <param name="result">The destination memory handle receiving the sum.</param>
    void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Subtracts one big integer from another in GPU device memory (<c>result = left - right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the minuend.</param>
    /// <param name="right">The memory handle of the subtrahend.</param>
    /// <param name="result">The destination memory handle receiving the difference.</param>
    void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Multiplies two big integers in GPU device memory (<c>result = left * right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the multiplicand.</param>
    /// <param name="right">The memory handle of the multiplier.</param>
    /// <param name="result">The destination memory handle receiving the product.</param>
    void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Divides one big integer by another in GPU device memory (<c>result = left / right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the dividend.</param>
    /// <param name="right">The memory handle of the divisor.</param>
    /// <param name="result">The destination memory handle receiving the quotient.</param>
    void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Computes the remainder of two big integers in GPU device memory (<c>result = left % right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the dividend.</param>
    /// <param name="right">The memory handle of the divisor.</param>
    /// <param name="result">The destination memory handle receiving the remainder.</param>
    void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Performs bitwise AND on two big integers in GPU device memory (<c>result = left &amp; right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the first operand.</param>
    /// <param name="right">The memory handle of the second operand.</param>
    /// <param name="result">The destination memory handle receiving the bitwise AND result.</param>
    void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Performs bitwise OR on two big integers in GPU device memory (<c>result = left | right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the first operand.</param>
    /// <param name="right">The memory handle of the second operand.</param>
    /// <param name="result">The destination memory handle receiving the bitwise OR result.</param>
    void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Performs bitwise XOR on two big integers in GPU device memory (<c>result = left ^ right</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="left">The memory handle of the first operand.</param>
    /// <param name="right">The memory handle of the second operand.</param>
    /// <param name="result">The destination memory handle receiving the bitwise XOR result.</param>
    void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    /// <summary>Performs bitwise NOT on a big integer in GPU device memory (<c>result = ~target</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="target">The memory handle of the operand to invert.</param>
    /// <param name="result">The destination memory handle receiving the bitwise NOT result.</param>
    void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, GpuBigIntMemoryHandle result);

    /// <summary>Shifts a big integer left by the specified bit count in GPU device memory (<c>result = target &lt;&lt; shiftBits</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="target">The memory handle of the operand to shift.</param>
    /// <param name="shiftBits">The bit shift amount.</param>
    /// <param name="result">The destination memory handle receiving the shifted value.</param>
    void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
        GpuBigIntMemoryHandle result);

    /// <summary>Shifts a big integer right by the specified bit count in GPU device memory (<c>result = target &gt;&gt; shiftBits</c>).</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="target">The memory handle of the operand to shift.</param>
    /// <param name="shiftBits">The bit shift amount.</param>
    /// <param name="result">The destination memory handle receiving the shifted value.</param>
    void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
        GpuBigIntMemoryHandle result);

    /// <summary>Computes the integer square root ($\lfloor\sqrt{\text{target}}\rfloor$) in GPU device memory.</summary>
    /// <param name="context">The GPU big integer execution context.</param>
    /// <param name="target">The memory handle of the non-negative operand.</param>
    /// <param name="result">The destination memory handle receiving the integer square root.</param>
    void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, GpuBigIntMemoryHandle result);
}
