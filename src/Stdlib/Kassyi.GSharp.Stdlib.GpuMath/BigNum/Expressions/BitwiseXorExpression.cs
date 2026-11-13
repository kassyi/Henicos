namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class BitwiseXorExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Xor)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseXor(ref context, first, second, result);
}
