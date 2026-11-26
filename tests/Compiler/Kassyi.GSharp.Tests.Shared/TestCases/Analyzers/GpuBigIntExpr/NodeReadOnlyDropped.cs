// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class NodeReadOnlyDropped
{
    public int InspectWidth(GpuBigInt a, GpuBigInt b)
    {
        GpuBigIntExpr sum = a + b;
        return sum.Node.NumWords;
    }
}
