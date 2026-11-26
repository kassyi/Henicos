// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class CompileToGraphExampleAccepted
{
    public void CompileExample(GpuWorkspace ws, GpuBigInt a, GpuBigInt b)
    {
        GpuBigIntExpr e = (GpuBigIntExpr)a + b;
        using var g = e.CompileToGraph(ws);
    }
}
