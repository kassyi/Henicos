namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

internal class GpuBigIntLeafExpression(GpuBigInteger value) : IGpuBigIntExpression
{
    public int NumWords => value.Value.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        if (targetResult == null)
        {
            return value; // Retain original if no explicit target provided
        }

        targetResult.CopyFrom(value.Value);
        return new GpuBigInteger(targetResult, value.IsNegative);
    }
}

internal abstract class SignedBinaryExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    protected IGpuBigIntExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    public virtual int NumWords
    {
        get
        {
            if (Left.NumWords != Right.NumWords)
            {
                throw new ArgumentException("Left and Right expressions must have the same number of words.");
            }

            return Left.NumWords;
        }
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        if (targetResult != null && targetResult.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        GpuBigInteger leftResult = default;
        GpuBigInteger rightResult = default;
        var finalBuffer = targetResult;

        try
        {
            leftResult = Left.Evaluate(context);
            rightResult = Right.Evaluate(context);
            if (finalBuffer == null)
            {
                finalBuffer = context.RentTemp(NumWords);
            }

            var result = Execute(leftResult, rightResult, finalBuffer);
            finalBuffer = null; // Evaluated successfully, do not return the result buffer
            return result;
        }
        finally
        {
            if (leftResult.Value != null && leftResult.Value.IsTemp)
            {
                context.ReturnTemp(leftResult.Value);
            }

            if (rightResult.Value != null && rightResult.Value.IsTemp)
            {
                context.ReturnTemp(rightResult.Value);
            }

            if (targetResult == null && finalBuffer != null)
            {
                context.ReturnTemp(finalBuffer);
            }
        }
    }

    protected abstract GpuBigInteger Execute(GpuBigInteger a, GpuBigInteger b, GpuUIntBigInteger resultBuffer);
}

internal class SignedAddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInteger Execute(GpuBigInteger a, GpuBigInteger b, GpuUIntBigInteger resultBuffer)
    {
        if (a.IsNegative == b.IsNegative)
        {
            GpuBigIntMath.Add(a.Value, b.Value, resultBuffer);
            return new GpuBigInteger(resultBuffer, a.IsNegative);
        }

        int cmp = a.Value.CompareTo(b.Value);
        if (cmp >= 0)
        {
            GpuBigIntMath.Sub(a.Value, b.Value, resultBuffer);
            return new GpuBigInteger(resultBuffer, cmp != 0 && a.IsNegative);
        }

        GpuBigIntMath.Sub(b.Value, a.Value, resultBuffer);
        return new GpuBigInteger(resultBuffer, b.IsNegative);
    }
}

internal class SignedSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInteger Execute(GpuBigInteger a, GpuBigInteger b, GpuUIntBigInteger resultBuffer)
    {
        if (a.IsNegative != b.IsNegative)
        {
            GpuBigIntMath.Add(a.Value, b.Value, resultBuffer);
            return new GpuBigInteger(resultBuffer, a.IsNegative);
        }

        int cmp = a.Value.CompareTo(b.Value);
        if (cmp >= 0)
        {
            GpuBigIntMath.Sub(a.Value, b.Value, resultBuffer);
            return new GpuBigInteger(resultBuffer, cmp != 0 && a.IsNegative);
        }

        GpuBigIntMath.Sub(b.Value, a.Value, resultBuffer);
        return new GpuBigInteger(resultBuffer, !a.IsNegative);
    }
}

internal class SignedMulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    public override int NumWords => Left.NumWords + Right.NumWords;

    protected override GpuBigInteger Execute(GpuBigInteger a, GpuBigInteger b, GpuUIntBigInteger resultBuffer)
    {
        GpuBigIntMath.Mul(a.Value, b.Value, resultBuffer);
        return new GpuBigInteger(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedDivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInteger Execute(GpuBigInteger a, GpuBigInteger b, GpuUIntBigInteger resultBuffer)
    {
        GpuBigIntDivRem.Div(a.Value, b.Value, resultBuffer);
        return new GpuBigInteger(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedNegateExpression(IGpuBigIntExpression target) : IGpuBigIntExpression
{
    private IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        var innerResult = Target.Evaluate(context, targetResult);
        return new GpuBigInteger(innerResult.Value, !innerResult.IsNegative);
    }
}

internal class SignedBitwiseNotExpression(IGpuBigIntExpression target) : IGpuBigIntExpression
{
    private IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        if (targetResult != null && targetResult.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        GpuBigInteger targetEval = default;
        GpuUIntBigInteger? finalBuffer = targetResult;

        try
        {
            targetEval = Target.Evaluate(context);
            if (finalBuffer == null)
            {
                finalBuffer = context.RentTemp(NumWords);
            }

            GpuBigIntMath.BitwiseNot(targetEval.Value, finalBuffer);
            var result = new GpuBigInteger(finalBuffer, targetEval.IsNegative);
            finalBuffer = null;
            return result;
        }
        finally
        {
            if (targetEval.Value != null && targetEval.Value.IsTemp)
            {
                context.ReturnTemp(targetEval.Value);
            }

            if (targetResult == null && finalBuffer != null)
            {
                context.ReturnTemp(finalBuffer);
            }
        }
    }
}
