// ReSharper disable All
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class UsingDeclarationAccepted
{
    public void BuildExpression(GpuBigInt a, GpuBigInt b)
    {
        using GpuBigIntExpr e = a + b;
    }
}
