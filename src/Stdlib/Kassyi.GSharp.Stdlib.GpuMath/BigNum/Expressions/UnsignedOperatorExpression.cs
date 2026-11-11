namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// An unsigned operator node: <see cref="BigIntExpressionVisitor"/> evaluates its operands with
/// <see cref="ExpressionWalk"/>, without recursion, and then applies the node.
/// </summary>
internal abstract class UnsignedOperatorExpression : IGpuBigUIntExpression, IBitBoundedExpression
{
    /// <summary>Gets the number of operands, 1 or 2.</summary>
    internal int OperandCount { get; }

    /// <summary>Gets the first operand.</summary>
    internal IGpuBigUIntExpression FirstOperand { get; }

    /// <summary>Gets the second operand; the first again for a unary node.</summary>
    internal IGpuBigUIntExpression SecondOperand { get; }

    /// <summary>Initializes a new instance of the <see cref="UnsignedOperatorExpression"/> class for a unary node.</summary>
    /// <param name="operand">The operand.</param>
    protected UnsignedOperatorExpression(IGpuBigUIntExpression operand)
    {
        FirstOperand = operand;
        SecondOperand = operand;
        OperandCount = 1;
    }

    /// <summary>Initializes a new instance of the <see cref="UnsignedOperatorExpression"/> class for a binary node.</summary>
    /// <param name="first">The left operand.</param>
    /// <param name="second">The right operand.</param>
    protected UnsignedOperatorExpression(IGpuBigUIntExpression first, IGpuBigUIntExpression second)
    {
        FirstOperand = first;
        SecondOperand = second;
        OperandCount = 2;
    }

    /// <summary>Gets the operand of a unary node.</summary>
    internal IGpuBigUIntExpression Target => FirstOperand;

    /// <inheritdoc/>
    public abstract long Bits { get; }

    /// <inheritdoc/>
    public virtual int NumWords => BigIntWidths.Words(Bits);

    /// <inheritdoc/>
    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.Walk(this);

    /// <summary>Applies the node to its evaluated operands.</summary>
    /// <param name="context">The execution context of the evaluation.</param>
    /// <param name="first">The first operand.</param>
    /// <param name="second">The second operand; the default handle for a unary node.</param>
    /// <param name="result">The result buffer, of <see cref="NumWords"/> words.</param>
    public abstract void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result);
}
