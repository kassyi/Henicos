
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A lightweight value type wrapper that constructs and manages a lazy expression tree of unsigned GPU BigInteger arithmetic and bitwise operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Allows natural C# mathematical operator syntax (<c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>&amp;</c>, <c>|</c>, <c>^</c>, <c>&lt;&lt;</c>, <c>&gt;&gt;</c>)
/// to produce composable AST nodes that can be visited and evaluated into temporary or dedicated GPU buffers.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly struct wrapping immutable AST nodes, no device memory is allocated until evaluated.
/// </para>
/// <para>
/// <b>Result widths:</b> a result never overflows. Every node has an upper bound of its result in bits, computed from
/// its operands when the expression is built, and the result has the words of that bound
/// (<see cref="IGpuBigUIntExpression.NumWords"/>): <c>a + b</c> one bit more than the wider operand, <c>a - b</c>
/// the left operand, <c>a * b</c> the sum of the operands, <c>a / b</c> the dividend, <c>a % b</c> and <c>a &amp; b</c>
/// the narrower operand, <c>a | b</c> and <c>a ^ b</c> the wider one, <c>a &lt;&lt; k</c> <c>k</c> bits more,
/// <c>a &gt;&gt; k</c> <c>k</c> bits fewer (at least one), <c>~a</c> every bit of the words of <c>a</c>, and the
/// square root half the bits. The operands may have different widths. Narrow a result explicitly with
/// <see cref="Fit"/> or <see cref="Truncate"/>.
/// </para>
/// <para>
/// <b>Domain errors:</b> a difference below zero, and a value that does not <see cref="Fit"/>, are reported as
/// <see cref="OverflowException"/> at the next read of the workspace stream (or by
/// <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode they are thrown at the evaluation.
/// </para>
/// </remarks>
public readonly struct GpuBigUIntExpr
{
    /// <summary>
    /// Gets the root AST node of this unsigned expression tree.
    /// </summary>
    public IGpuBigUIntExpression Node
    {
        get;
    }

    internal GpuBigUIntExpr(IGpuBigUIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>
    /// Implicitly wraps a <see cref="GpuBigUInt"/> buffer as an unsigned expression leaf node.
    /// </summary>
    /// <param name="b">The unsigned big integer buffer.</param>
    public static implicit operator GpuBigUIntExpr(GpuBigUInt b)
        => new(b);

    /// <summary>
    /// Constructs a lazy addition expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The left unsigned expression operand.</param>
    /// <param name="right">The right unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the addition AST node.</returns>
    public static GpuBigUIntExpr operator +(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new AddExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy subtraction expression between two unsigned big integer expressions. A difference below zero
    /// is a domain error.
    /// </summary>
    /// <param name="left">The minuend unsigned expression operand.</param>
    /// <param name="right">The subtrahend unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the subtraction AST node.</returns>
    public static GpuBigUIntExpr operator -(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new SubExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy multiplication expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The multiplicand unsigned expression operand.</param>
    /// <param name="right">The multiplier unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the multiplication AST node.</returns>
    public static GpuBigUIntExpr operator *(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new MulExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy division expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The dividend unsigned expression operand.</param>
    /// <param name="right">The divisor unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the division AST node.</returns>
    public static GpuBigUIntExpr operator /(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new DivExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy remainder (modulo) expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The dividend unsigned expression operand.</param>
    /// <param name="right">The divisor unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the modulo AST node.</returns>
    public static GpuBigUIntExpr operator %(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new ModExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy square root expression for the specified unsigned big integer expression.
    /// </summary>
    /// <param name="expr">The radicand unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the square root AST node.</returns>
    public static GpuBigUIntExpr Sqrt(GpuBigUIntExpr expr)
        => new(new SqrtExpression(expr.Node));

    /// <summary>
    /// Constructs a lazy bitwise AND expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The left unsigned expression operand.</param>
    /// <param name="right">The right unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the bitwise AND AST node.</returns>
    public static GpuBigUIntExpr operator &(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseAndExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy bitwise OR expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The left unsigned expression operand.</param>
    /// <param name="right">The right unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the bitwise OR AST node.</returns>
    public static GpuBigUIntExpr operator |(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseOrExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy bitwise XOR expression between two unsigned big integer expressions.
    /// </summary>
    /// <param name="left">The left unsigned expression operand.</param>
    /// <param name="right">The right unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the bitwise XOR AST node.</returns>
    public static GpuBigUIntExpr operator ^(GpuBigUIntExpr left, GpuBigUIntExpr right)
        => new(new BitwiseXorExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy bitwise NOT expression for the specified unsigned big integer expression.
    /// </summary>
    /// <param name="left">The unsigned expression operand.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the bitwise NOT AST node.</returns>
    public static GpuBigUIntExpr operator ~(GpuBigUIntExpr left)
        => new(new BitwiseNotExpression(left.Node));

    /// <summary>
    /// Constructs a lazy left bit-shift expression for the specified unsigned big integer expression.
    /// </summary>
    /// <param name="left">The unsigned expression operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift left; a negative count shifts right, as
    /// <see cref="BigInteger"/>. The result keeps every bit.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the left-shift AST node.</returns>
    public static GpuBigUIntExpr operator <<(GpuBigUIntExpr left, int shiftBits)
        => new(new ShiftLeftExpression(left.Node, shiftBits));

    /// <summary>
    /// Constructs a lazy right bit-shift expression for the specified unsigned big integer expression.
    /// </summary>
    /// <param name="left">The unsigned expression operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift right; a negative count shifts left, as
    /// <see cref="BigInteger"/>.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> representing the right-shift AST node.</returns>
    public static GpuBigUIntExpr operator >>(GpuBigUIntExpr left, int shiftBits)
        => new(new ShiftRightExpression(left.Node, shiftBits));

    /// <summary>
    /// Narrows this expression to <paramref name="words"/> words, unchecked: the value modulo
    /// <c>2^(32 * words)</c>, as a C# <c>unchecked</c> narrowing. A wider word count zero-extends.
    /// </summary>
    /// <param name="words">The word count of the result, at least 1.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> whose result has <paramref name="words"/> words.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="words"/> is below 1.</exception>
    public GpuBigUIntExpr Truncate(int words)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        return new GpuBigUIntExpr(new NarrowExpression(Node, words, isChecked: false));
    }

    /// <summary>
    /// Narrows this expression to <paramref name="words"/> words, checked: a value that does not fit is a domain
    /// error, as a C# <c>checked</c> narrowing.
    /// </summary>
    /// <param name="words">The word count of the result, at least 1.</param>
    /// <returns>A new <see cref="GpuBigUIntExpr"/> whose result has <paramref name="words"/> words.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="words"/> is below 1.</exception>
    /// <remarks>
    /// On the GPU the domain error is reported as <see cref="OverflowException"/> at the next read of the workspace
    /// stream (or by <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode it is thrown when the
    /// expression is evaluated.
    /// </remarks>
    public GpuBigUIntExpr Fit(int words)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        return new GpuBigUIntExpr(new NarrowExpression(Node, words, isChecked: true));
    }
}



