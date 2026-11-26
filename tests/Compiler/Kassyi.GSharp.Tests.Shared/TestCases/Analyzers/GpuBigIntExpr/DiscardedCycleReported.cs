// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class DiscardedCycleReported
{
    public void CyclicCopiesWithoutEvaluation(GpuBigInt x, GpuBigInt y)
    {
        GpuBigIntExpr a = x + y;
        GpuBigIntExpr b = a;
        a = b;

        GpuBigIntExpr self = x;
        self = self;
    }
}
