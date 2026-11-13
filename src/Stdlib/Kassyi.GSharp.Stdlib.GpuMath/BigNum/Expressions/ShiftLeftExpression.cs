namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// a << k: a + k bits; a negative count shifts right, as BigInteger
internal class ShiftLeftExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftLeft(IBitBoundedExpression.Of(target), shiftBits);

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.ShiftLeft(ref context, first, ShiftBits, result);
}
