namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SubExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.UnsignedSubtract)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Sub(ref context, first, second, result);
}
