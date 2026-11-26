// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class ConditionalNodeReadOnlyDropped
{
    public int ReadConditionalNodeNumWords(GpuBigInt a, GpuBigInt b)
    {
        GpuBigIntExpr? opt1 = a + b;
        int w1 = opt1?.Node.NumWords ?? 0;

        GpuBigIntExpr? opt2 = a + b;
        int w2 = opt2?.Node?.NumWords ?? 0;

        return w1 + w2;
    }
}
