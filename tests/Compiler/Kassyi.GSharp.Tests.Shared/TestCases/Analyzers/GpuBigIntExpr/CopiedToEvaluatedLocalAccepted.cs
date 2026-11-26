// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class CopiedToEvaluatedLocalAccepted
{
    public void CopyAndEvaluate(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        GpuBigIntExpr first = a + b;
        GpuBigIntExpr second = first;
        second.Evaluate(dest);
    }
}
