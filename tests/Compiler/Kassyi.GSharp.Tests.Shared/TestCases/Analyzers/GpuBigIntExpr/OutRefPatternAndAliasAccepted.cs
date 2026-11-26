// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class OutRefPatternAndAliasAccepted
{
    public void TakesOut(out GpuBigIntExpr e, GpuBigInt a, GpuBigInt b)
    {
        e = a + b;
    }

    public void TakesOutViaLocal(out GpuBigIntExpr e, GpuBigInt a, GpuBigInt b)
    {
        GpuBigIntExpr tmp = a + b;
        e = tmp;
    }

    public void TakesRef(ref GpuBigIntExpr r, GpuBigInt a, GpuBigInt b)
    {
        r = a + b;
    }

    public void UseAllAcceptedForms(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        TakesOut(out var outEval, a, b);
        outEval.Evaluate(dest);

        TakesOut(out GpuBigIntExpr _, a, b);

        GpuBigIntExpr aliased = a + b;
        ref GpuBigIntExpr aliasRef = ref aliased;
        aliasRef.Evaluate(dest);

        GpuBigIntExpr? opt1 = a + b;
        if (opt1 is { } val1)
        {
            val1.Evaluate(dest);
        }

        GpuBigIntExpr? opt2 = a + b;
        if (opt2 is GpuBigIntExpr val2)
        {
            val2.Evaluate(dest);
        }

        GpuBigIntExpr? opt3 = a + b;
        _ = opt3 switch
        {
            { } v3 => v3.Evaluate(dest),
            _ => default
        };

        GpuBigIntExpr? opt4 = a + b;
        switch (opt4)
        {
            case { } v4:
                v4.Evaluate(dest);
                break;
        }
    }
}
