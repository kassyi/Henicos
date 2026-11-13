namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class AddExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Add)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Add(ref context, first, second, result);
}
