namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class BitwiseAndExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.And)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseAnd(ref context, first, second, result);
}
