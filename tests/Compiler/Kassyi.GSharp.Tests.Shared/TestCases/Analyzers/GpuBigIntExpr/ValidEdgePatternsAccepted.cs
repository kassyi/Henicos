// ReSharper disable All
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public class ValidEdgePatternsAccepted
{
    public void OperatorOperandAndReassignment(GpuBigInt a, GpuBigInt b, GpuBigUInt dest)
    {
        GpuBigIntExpr ea = a;
        GpuBigIntExpr eb = b;
        GpuBigIntExpr combined = ~ea + eb;
        for (int i = 0; i < 2; i++)
        {
            combined = a + combined;
        }

        combined.Evaluate(dest);
    }

    public void LambdaUsingAndTakesOwnership(GpuWorkspace ws, GpuBigInt a, GpuBigInt b,
        IGpuBigIntVectorExpression vector)
    {
        GpuBigIntExpr inLambda = a + b;
        Action act = () => inLambda.Evaluate(ws);

        GpuBigIntExpr inUsingStmt = a + b;
        using (inUsingStmt)
        {
        }

        var scalarSum = (GpuBigIntExpr)a + b;
        ConsumeNode(scalarSum.Node);

        act();
    }

    private static void ConsumeNode([Kassyi.GSharp.Core.TakesOwnership] IGpuBigIntExpression node)
    {
    }

    public void ValuePropertyAssignment(GpuBigInt a, GpuBigInt b, ExprSink sink)
    {
        GpuBigIntExpr expr = a + b;
        sink.Value = expr;
    }

    public sealed class ExprSink
    {
        public GpuBigIntExpr Value { get; set; }
    }
}
