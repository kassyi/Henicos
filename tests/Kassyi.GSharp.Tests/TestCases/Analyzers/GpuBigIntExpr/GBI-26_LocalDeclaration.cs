namespace Kassyi.GSharp.Tests.TestCases.Analyzers.GBI_26_LocalDeclaration;

using Kassyi.GSharp.Stdlib.Math.Expressions;

public class TestClass
{
    public void Method(GpuBigIntExpr expr)
    {
        var local = expr;
    }
}
