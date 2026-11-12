namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class SignedAddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: false)
{
    public override SignedOperation Operation => SignedOperation.Add;
}
