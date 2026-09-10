
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

/// <summary>Abstract base AST node representing a binary GPU BigInteger operation.</summary>
internal abstract class BinaryExpression : IGpuBigUIntExpression
{
    protected IGpuBigUIntExpression Left
    {
        get;
    }
    protected IGpuBigUIntExpression Right
    {
        get;
    }

    public int NumWords
    {
        get;
    }

    protected BinaryExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        if (left.NumWords != right.NumWords)
        {
            throw new ArgumentException("Left and Right expressions must have the same number of words.");
        }

        NumWords = left.NumWords;
    }

    public GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null) =>
        ExpressionEvaluator.EvaluateBinary(context, Left, Right, targetResult, NumWords, Execute);

    protected abstract void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result);
}

internal class AddExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntMath.Add(a, b, result);
}

internal class SubExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntMath.Sub(a, b, result);
}

internal class MulExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntMath.Mul(a, b, result);
}

internal class DivExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntDivRem.Div(a, b, result);
}

internal class BitwiseAndExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntMath.BitwiseAnd(a, b, result);
}

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits) : IGpuBigUIntExpression
{
    protected IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    protected int ShiftBits { get; } = shiftBits;
    public int NumWords => Target.NumWords;

    public GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null) =>
        ExpressionEvaluator.EvaluateUnary(context, Target, targetResult, NumWords, Execute);

    protected abstract void Execute(GpuUIntBigInteger target, GpuUIntBigInteger result);
}

internal class ShiftLeftExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuUIntBigInteger target, GpuUIntBigInteger result) =>
        GpuBigIntMath.ShiftLeft(target, ShiftBits, result);
}

internal class ShiftRightExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuUIntBigInteger target, GpuUIntBigInteger result) =>
        GpuBigIntMath.ShiftRight(target, ShiftBits, result);
}

internal class SqrtExpression(IGpuBigUIntExpression target) : IGpuBigUIntExpression
{
    protected IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null) =>
        ExpressionEvaluator.EvaluateUnary(context, Target, targetResult, NumWords, GpuBigIntDivRem.Sqrt);
}

internal class ModExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger a, GpuUIntBigInteger b, GpuUIntBigInteger result) =>
        GpuBigIntDivRem.Mod(a, b, result);
}

internal class BitwiseOrExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        GpuBigIntMath.BitwiseOr(leftOperand, rightOperand, resultBuffer);
}

internal class BitwiseXorExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        GpuBigIntMath.BitwiseXor(leftOperand, rightOperand, resultBuffer);
}

internal class BitwiseNotExpression(IGpuBigUIntExpression left) : IGpuBigUIntExpression
{
    public int NumWords => left.NumWords;

    public GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null) =>
        ExpressionEvaluator.EvaluateUnary(context, left, targetResult, NumWords, GpuBigIntMath.BitwiseNot);
}



