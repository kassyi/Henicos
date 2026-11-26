// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class ReturnedFromHelperAccepted
{
    public GpuBigIntExpr BuildExpression(GpuBigInt a, GpuBigInt b)
    {
        GpuBigIntExpr e = a + b;
        return e;
    }
}
