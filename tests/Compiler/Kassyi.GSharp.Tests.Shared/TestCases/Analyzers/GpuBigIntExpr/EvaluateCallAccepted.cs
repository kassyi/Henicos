// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class EvaluateCallAccepted
{
    public void EvaluateExpression(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        GpuBigIntExpr e = a + b;
        e.Evaluate(dest);
    }
}
