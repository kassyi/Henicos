namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SqrtExpression(IGpuBigUIntExpression target)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
{
    public override long Bits { get; } = BigIntWidths.Sqrt(IBitBoundedExpression.Of(target));

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Sqrt(ref context, first, result);
}
