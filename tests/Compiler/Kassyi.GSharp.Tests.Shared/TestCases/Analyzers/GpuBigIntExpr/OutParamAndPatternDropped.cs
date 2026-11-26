// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class OutParamAndPatternDropped
{
    public void TakesOut(out GpuBigIntExpr e, GpuBigInt a, GpuBigInt b)
    {
        e = a + b;
    }

    public void ReassignValueParam(GpuBigIntExpr p, GpuBigInt a, GpuBigInt b)
    {
        p = a + b;
    }

    public void DroppedPatterns(GpuBigInt a, GpuBigInt b)
    {
        TakesOut(out var outUnused, a, b);
        TakesOut(out GpuBigIntExpr outTypedUnused, a, b);

        GpuBigIntExpr? optDropped = a + b;
        if (optDropped is { } val)
        {
            _ = val.Node.NumWords;
        }

        GpuBigIntExpr unEvaluated = a + b;
        ref GpuBigIntExpr unusedAlias = ref unEvaluated;

        var dead = a + b;
        _ = -dead;
    }
}
