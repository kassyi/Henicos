namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
{
    internal int ShiftBits { get; } = shiftBits;
}
