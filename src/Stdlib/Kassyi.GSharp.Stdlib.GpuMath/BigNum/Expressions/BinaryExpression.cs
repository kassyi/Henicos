


namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Abstract base AST node representing a binary GPU BigInteger operation between two operand subtrees.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Enforces dimension validation between operands (<see cref="NumWords"/> equality) and standardizes binary AST dispatch.
/// </para>
/// </remarks>
internal abstract class BinaryExpression : IGpuBigUIntExpression
{
    internal IGpuBigUIntExpression Left
    {
        get;
    }
    internal IGpuBigUIntExpression Right
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
    protected abstract void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result);

    public abstract ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}


internal class AddExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntMath.Add(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitAdd(this);
}

internal class SubExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntMath.Sub(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitSub(this);
}

internal class MulExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntMath.Mul(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitMul(this);
}

internal class DivExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntDivRem.Div(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitDiv(this);
}

internal class BitwiseAndExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntMath.BitwiseAnd(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseAnd(this);
}

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits) : IGpuBigUIntExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    internal int ShiftBits { get; } = shiftBits;
    public int NumWords => Target.NumWords;

    protected abstract void Execute(GpuBigUInt target, GpuBigUInt result);

    public abstract ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}

internal class ShiftLeftExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuBigUInt target, GpuBigUInt result) =>
        GpuBigIntMath.ShiftLeft(target, ShiftBits, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitShiftLeft(this);
}

internal class ShiftRightExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    protected override void Execute(GpuBigUInt target, GpuBigUInt result) =>
        GpuBigIntMath.ShiftRight(target, ShiftBits, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitShiftRight(this);
}

internal class SqrtExpression(IGpuBigUIntExpression target) : IGpuBigUIntExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitSqrt(this);
}

internal class ModExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right) : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result) =>
        GpuBigIntDivRem.Mod(a, b, result);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitMod(this);
}

internal class BitwiseOrExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        GpuBigIntMath.BitwiseOr(leftOperand, rightOperand, resultBuffer);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseOr(this);
}

internal class BitwiseXorExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right)
{
    protected override void Execute(GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        GpuBigIntMath.BitwiseXor(leftOperand, rightOperand, resultBuffer);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseXor(this);
}

internal class BitwiseNotExpression(IGpuBigUIntExpression left) : IGpuBigUIntExpression
{
    internal IGpuBigUIntExpression Target { get; } = left;
    public int NumWords => Target.NumWords;

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseNot(this);
}



