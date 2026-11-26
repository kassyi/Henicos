// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class EscapedAndLoopVariableAccepted
{
    public int ForEachReadsNode(GpuBigIntExpr[] exprs)
    {
        int n = 0;
        foreach (var e in exprs)
        {
            n += e.Node.NumWords;
        }
        return n;
    }

    public void TupleEscape(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        var e = a + b;
        var t = (e, 0);
        t.Item1.Evaluate(dest);
    }

    public void CollectionEscape(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        var e = a + b;
        GpuBigIntExpr[] xs = [e];
        xs[0].Evaluate(dest);
    }

    public void LambdaCapture(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        var e = a + b;
        System.Action run = () => e.Evaluate(dest);
        run();
    }
}
