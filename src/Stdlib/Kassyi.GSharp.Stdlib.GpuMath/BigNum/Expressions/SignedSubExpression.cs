namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class SignedSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: true)
{
    public override SignedOperation Operation => SignedOperation.Subtract;
}
