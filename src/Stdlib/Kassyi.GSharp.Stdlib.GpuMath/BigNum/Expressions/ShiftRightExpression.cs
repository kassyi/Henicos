namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// a >> k: max(a - k, 1) bits; a negative count shifts left, as BigInteger
internal class ShiftRightExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftRight(IBitBoundedExpression.Of(target), shiftBits);

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.ShiftRight(ref context, first, ShiftBits, result);
}
