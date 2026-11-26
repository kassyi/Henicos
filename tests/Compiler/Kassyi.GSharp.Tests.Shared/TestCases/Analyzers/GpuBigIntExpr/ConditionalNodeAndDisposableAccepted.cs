// ReSharper disable All
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class ConditionalNodeAndDisposableAccepted
{
    public void ConditionalNodeAndDisposable(GpuWorkspace ws, GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        GpuBigIntExpr? opt1 = a + b;
        opt1?.Node.Evaluate(ws, dest);

        GpuBigIntExpr? opt2 = a + b;
        opt2?.Node?.Evaluate(ws, dest);

        GpuBigIntExpr? opt3 = a + b;
        (opt3?.Node as IDisposable)?.Dispose();

        GpuBigIntExpr e1 = a + b;
        (e1.Node as IDisposable)?.Dispose();

        GpuBigIntExpr e2 = a + b;
        e2.Node?.Evaluate(ws, dest);

        GpuBigIntExpr e3 = a + b;
        using var d1 = (IDisposable)e3;

        GpuBigIntExpr e4 = a + b;
        using (IDisposable d2 = e4)
        {
        }
    }
}
