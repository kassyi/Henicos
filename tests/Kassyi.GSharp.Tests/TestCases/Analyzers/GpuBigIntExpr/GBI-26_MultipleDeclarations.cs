namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GBI_26_MultipleDeclarations;

using Kassyi.GSharp.Stdlib.Math.Expressions;

public class TestClass
{
    public void Method(GpuBigIntExpr expr)
    {
        GpuBigIntExpr local1 = expr, local2 = expr;
    }
}
