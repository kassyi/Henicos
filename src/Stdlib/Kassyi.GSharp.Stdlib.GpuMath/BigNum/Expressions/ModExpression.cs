namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class ModExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Remainder)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Mod(ref context, first, second, result);
}
