namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

internal static class ExpressionEvaluator
{
    public static GpuUIntBigInteger EvaluateBinary(
        GpuContext context,
        IGpuBigUIntExpression left,
        IGpuBigUIntExpression right,
        GpuUIntBigInteger? targetResult,
        int numWords,
        Action<GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger> execute)
    {
        var leftResult = left.Evaluate(context);
        var rightResult = right.Evaluate(context);

        // [WHY] Rent temporary GPU memory buffers from the pool during AST evaluation
        // to avoid costly device memory allocation calls (cuMemAlloc) in expression chains.
        var finalResult = targetResult ?? context.RentTemp(numWords);

        try
        {
            execute(leftResult, rightResult, finalResult);
            return finalResult;
        }
        finally
        {
            // Return temporaries back to pool if they were rented
            if (leftResult.IsTemp)
            {
                context.ReturnTemp(leftResult);
            }

            if (rightResult.IsTemp)
            {
                context.ReturnTemp(rightResult);
            }
        }
    }

    public static GpuUIntBigInteger EvaluateUnary(
        GpuContext context,
        IGpuBigUIntExpression target,
        GpuUIntBigInteger? targetResult,
        int numWords,
        Action<GpuUIntBigInteger, GpuUIntBigInteger> execute)
    {
        var targetEval = target.Evaluate(context);
        var finalResult = targetResult ?? context.RentTemp(numWords);

        try
        {
            execute(targetEval, finalResult);
            return finalResult;
        }
        finally
        {
            if (targetEval.IsTemp)
            {
                context.ReturnTemp(targetEval);
            }
        }
    }
}
