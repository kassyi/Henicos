namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class GpuBigIntLeafExpression(GpuBigInt value) : IGpuBigIntExpression
{
    public int NumWords => value.Value.NumWords;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (!targetResult.HasValue)
        {
            return value; // Retain original if no explicit target provided
        }

        targetResult.Value.CopyFrom(value.Value);
        return new GpuBigInt(targetResult.Value, value.IsNegative);
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

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (targetResult.HasValue && targetResult.Value.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        GpuBigInt leftResult = default;
        GpuBigInt rightResult = default;
        var finalBuffer = targetResult;

        try
        {
            leftResult = Left.Evaluate(workspace);
            rightResult = Right.Evaluate(workspace);
            if (!finalBuffer.HasValue)
            {
                finalBuffer = workspace.RentTemp(NumWords);
            }

            var result = Execute(leftResult, rightResult, finalBuffer.Value);
            finalBuffer = null; // Evaluated successfully, do not return the result buffer
            return result;
        }
        finally
        {
            if (true && leftResult.Value.IsTemp)
            {
                workspace.ReturnTemp(leftResult.Value);
            }

            if (true && rightResult.Value.IsTemp)
            {
                workspace.ReturnTemp(rightResult.Value);
            }

            if (!targetResult.HasValue && finalBuffer.HasValue)
            {
                workspace.ReturnTemp(finalBuffer.Value);
            }
        }
    }

    protected abstract GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer);
}

internal class SignedAddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        if (a.IsNegative == b.IsNegative)
        {
            GpuBigIntMath.Add(a.Value, b.Value, resultBuffer);
            return new GpuBigInt(resultBuffer, a.IsNegative);
        }

        var cmp = a.Value.CompareTo(b.Value);
        if (cmp >= 0)
        {
            GpuBigIntMath.Sub(a.Value, b.Value, resultBuffer);
            return new GpuBigInt(resultBuffer, cmp != 0 && a.IsNegative);
        }

        GpuBigIntMath.Sub(b.Value, a.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, b.IsNegative);
    }
}

internal class SignedSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        if (a.IsNegative != b.IsNegative)
        {
            GpuBigIntMath.Add(a.Value, b.Value, resultBuffer);
            return new GpuBigInt(resultBuffer, a.IsNegative);
        }

        var cmp = a.Value.CompareTo(b.Value);
        if (cmp >= 0)
        {
            GpuBigIntMath.Sub(a.Value, b.Value, resultBuffer);
            return new GpuBigInt(resultBuffer, cmp != 0 && a.IsNegative);
        }

        GpuBigIntMath.Sub(b.Value, a.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, !a.IsNegative);
    }
}

internal class SignedMulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    public override int NumWords => Left.NumWords + Right.NumWords;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        GpuBigIntMath.Mul(a.Value, b.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedDivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        GpuBigIntDivRem.Div(a.Value, b.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedNegateExpression(IGpuBigIntExpression target) : IGpuBigIntExpression
{
    private IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var innerResult = Target.Evaluate(workspace, targetResult);
        return new GpuBigInt(innerResult.Value, !innerResult.IsNegative);
    }
}

internal abstract class SignedUnaryExpression(IGpuBigIntExpression target) : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public virtual int NumWords => Target.NumWords;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (targetResult.HasValue && targetResult.Value.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        GpuBigInt targetEval = default;
        var finalBuffer = targetResult;

        try
        {
            targetEval = Target.Evaluate(workspace);
            if (!finalBuffer.HasValue)
            {
                finalBuffer = workspace.RentTemp(NumWords);
            }

            var result = Execute(targetEval, finalBuffer.Value);
            finalBuffer = null;
            return result;
        }
        finally
        {
            if (targetEval.Value.IsTemp)
            {
                workspace.ReturnTemp(targetEval.Value);
            }

            if (!targetResult.HasValue && finalBuffer.HasValue)
            {
                workspace.ReturnTemp(finalBuffer.Value);
            }
        }
    }

    protected abstract GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer);
}

internal class SignedBitwiseNotExpression(IGpuBigIntExpression target) : SignedUnaryExpression(target)
{
    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        GpuBigIntMath.BitwiseNot(targetEval.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}
