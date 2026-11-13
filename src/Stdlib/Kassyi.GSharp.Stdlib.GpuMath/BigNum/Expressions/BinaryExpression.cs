namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Unsigned expression nodes. Every node has a static upper bound of its result in bits (BigIntWidths), computed from
// the bounds of its children when the node is built; its result has the words of that bound, so no node drops a carry,
// the top words of a product or the bits of a left shift. A difference below zero is a domain error.

/// <summary>
/// Abstract base AST node representing a binary GPU BigInteger operation between two operand subtrees.
/// </summary>
/// <remarks>
/// The operands may have different widths; the result has the words of the node's bound.
/// </remarks>
internal abstract class BinaryExpression : UnsignedOperatorExpression
{
    protected BinaryExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right, Func<long, long, long> bound)
        : base(
            left ?? throw new ArgumentNullException(nameof(left)),
            right ?? throw new ArgumentNullException(nameof(right))
        )
    {
        // The children are immutable and know their bounds already, so the bound is computed once, here
        Bits = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));
    }

    internal IGpuBigUIntExpression Left => FirstOperand;

    internal IGpuBigUIntExpression Right => SecondOperand;

    /// <inheritdoc/>
    public override long Bits { get; }
}
