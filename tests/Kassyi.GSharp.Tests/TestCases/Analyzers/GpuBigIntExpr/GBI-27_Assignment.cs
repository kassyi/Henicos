namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GBI_27_Assignment;

using Kassyi.GSharp.Stdlib.Math.Expressions;

public class TestClass
{
    private GpuBigIntExpr _field;

    public void Method(GpuBigIntExpr expr)
    {
        _field = expr;
    }
}
