namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// ~a: every bit of the words of a is inverted; the width of a defines the value
internal class BitwiseNotExpression(IGpuBigUIntExpression left)
    : UnsignedOperatorExpression(left ?? throw new ArgumentNullException(nameof(left)))
{
    public override long Bits { get; } = BigIntWidths.UnsignedNot(IBitBoundedExpression.Of(left));

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseNot(ref context, first, result);
}
