
namespace Kassyi.GSharp.Stdlib.Math.Expressions;

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
        GpuBigInteger.Add(a, b, result);
}

internal class SubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigInteger.Sub(a, b, result);
}

internal class MulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigInteger.Mul(a, b, result);
}

internal class DivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigInteger.Div(a, b, result);
}

internal class BitwiseAndExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        GpuBigInteger.BitwiseAnd(a, b, result);
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
        GpuBigInteger.ShiftLeft(target, ShiftBits, result);
}

internal class ShiftRightExpression(IGpuBigIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuBigInteger target, GpuBigInteger result) =>
        GpuBigInteger.ShiftRight(target, ShiftBits, result);
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
            GpuBigInteger.Sqrt(targetEval, finalResult);
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
        GpuBigInteger.Mod(a, b, result);
}

internal class BitwiseOrExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        GpuBigInteger.BitwiseOr(leftOperand, rightOperand, resultBuffer);
}

internal class BitwiseXorExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        GpuBigInteger.BitwiseXor(leftOperand, rightOperand, resultBuffer);
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
            GpuBigInteger.BitwiseNot(leftEval, targetEval);
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
