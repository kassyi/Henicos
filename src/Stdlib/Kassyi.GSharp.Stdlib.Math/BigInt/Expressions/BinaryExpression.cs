
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>Abstract base AST node representing a binary GPU BigInteger operation.</summary>
internal abstract class BinaryExpression : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Left
    {
        get;
    }
    protected IGpuBigIntExpression Right
    {
        get;
    }

    public int NumWords
    {
        get;
    }

    protected BinaryExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        if (left.NumWords != right.NumWords)
        {
            throw new ArgumentException("Left and Right expressions must have the same number of words.");
        }

        NumWords = left.NumWords;
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        var leftResult = Left.Evaluate(context);
        var rightResult = Right.Evaluate(context);

        // [WHY] Rent temporary GPU memory buffers from the pool during AST evaluation
        // to avoid costly device memory allocation calls (cuMemAlloc) in expression chains.
        var finalResult = targetResult ?? context.RentTemp(NumWords);

        try
        {
            Execute(leftResult, rightResult, finalResult);
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

    protected abstract void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
}

internal class AddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntMath.Add(a, b, result);
}

internal class SubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntMath.Sub(a, b, result);
}

internal class MulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntMath.Mul(a, b, result);
}

internal class DivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntDivRem.Div(a, b, result);
}

internal class BitwiseAndExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntMath.BitwiseAnd(a, b, result);
}

internal abstract class ShiftExpression(IGpuBigIntExpression target, int shiftBits) : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    protected int ShiftBits { get; } = shiftBits;
    public int NumWords => Target.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        var targetEval = Target.Evaluate(context);
        var finalResult = targetResult ?? context.RentTemp(NumWords);

        try
        {
            Execute(targetEval, finalResult);
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

    protected abstract void Execute(GpuBigInteger target, GpuBigInteger result);
}

internal class ShiftLeftExpression(IGpuBigIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuBigInteger target, GpuBigInteger result) =>
        GpuBigIntMath.ShiftLeft(target, ShiftBits, result);
}

internal class ShiftRightExpression(IGpuBigIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuBigInteger target, GpuBigInteger result) =>
        GpuBigIntMath.ShiftRight(target, ShiftBits, result);
}

internal class SqrtExpression(IGpuBigIntExpression target) : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        var targetEval = Target.Evaluate(context);
        var finalResult = targetResult ?? context.RentTemp(NumWords);

        try
        {
            GpuBigIntDivRem.Sqrt(targetEval, finalResult);
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

internal class ModExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigIntDivRem.Mod(a, b, result);
}

internal class BitwiseOrExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        GpuBigIntMath.BitwiseOr(leftOperand, rightOperand, resultBuffer);
}

internal class BitwiseXorExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        GpuBigIntMath.BitwiseXor(leftOperand, rightOperand, resultBuffer);
}

internal class BitwiseNotExpression(IGpuBigIntExpression left) : IGpuBigIntExpression
{
    public int NumWords => left.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        var targetEval = targetResult ?? context.RentTemp(NumWords);
        var leftEval = left.Evaluate(context, targetEval);
        try
        {
            GpuBigIntMath.BitwiseNot(leftEval, targetEval);
            return targetEval;
        }
        finally
        {
            if (leftEval.IsTemp)
            {
                context.ReturnTemp(leftEval);
            }
        }
    }
}



