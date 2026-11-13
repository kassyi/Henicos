namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class BitwiseOrExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Or)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseOr(ref context, first, second, result);
}
