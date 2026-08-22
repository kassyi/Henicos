using System;

namespace Kassyi.GSharp.GpuBigInt.Expressions;

internal abstract class BinaryExpression : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Left { get; }
    protected IGpuBigIntExpression Right { get; }

    public int NumWords { get; }

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
        
        Execute(leftResult, rightResult, finalResult);

        // Return temporaries back to pool if they were rented
        if (leftResult.IsTemp) context.ReturnTemp(leftResult);
        if (rightResult.IsTemp) context.ReturnTemp(rightResult);

        return finalResult;
    }

    protected abstract void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
}

internal class AddExpression : BinaryExpression
{
    public AddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : base(left, right) { }
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) => GpuBigInteger.Add(a, b, result);
}

internal class SubExpression : BinaryExpression
{
    public SubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : base(left, right) { }
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) => GpuBigInteger.Sub(a, b, result);
}

internal class BitwiseAndExpression : BinaryExpression
{
    public BitwiseAndExpression(IGpuBigIntExpression left, IGpuBigIntExpression right) : base(left, right) { }
    protected override void Execute(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) => GpuBigInteger.BitwiseAnd(a, b, result);
}

internal abstract class ShiftExpression : IGpuBigIntExpression
{
    protected IGpuBigIntExpression Target { get; }
    protected int ShiftBits { get; }
    public int NumWords => Target.NumWords;

    protected ShiftExpression(IGpuBigIntExpression target, int shiftBits)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ShiftBits = shiftBits;
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        var targetEval = Target.Evaluate(context);
        var finalResult = targetResult ?? context.RentTemp(NumWords);
        
        Execute(targetEval, finalResult);

        if (targetEval.IsTemp) context.ReturnTemp(targetEval);

        return finalResult;
    }

    protected abstract void Execute(GpuBigInteger target, GpuBigInteger result);
}

internal class ShiftLeftExpression : ShiftExpression
{
    public ShiftLeftExpression(IGpuBigIntExpression target, int shiftBits) : base(target, shiftBits) { }
    protected override void Execute(GpuBigInteger target, GpuBigInteger result) => GpuBigInteger.ShiftLeft(target, ShiftBits, result);
}

internal class ShiftRightExpression : ShiftExpression
{
    public ShiftRightExpression(IGpuBigIntExpression target, int shiftBits) : base(target, shiftBits) { }
    protected override void Execute(GpuBigInteger target, GpuBigInteger result) => GpuBigInteger.ShiftRight(target, ShiftBits, result);
}
