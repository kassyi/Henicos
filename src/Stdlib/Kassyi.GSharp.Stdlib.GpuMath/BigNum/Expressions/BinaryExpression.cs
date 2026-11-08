namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Unsigned expression nodes. Every node has a static upper bound of its result in bits (BigIntWidths), computed from
// the bounds of its children when the node is built; its result has the words of that bound, so no node drops a carry,
// the top words of a product or the bits of a left shift. A difference below zero is a domain error.

/// <summary>
/// Abstract base AST node representing a binary GPU BigInteger operation between two operand subtrees.
/// </summary>
/// <remarks>
/// The operands may have different widths; the result has the words of the node's bound.
/// </remarks>
internal abstract class BinaryExpression : IGpuBigUIntExpression, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Left
    {
        get;
    }

    internal IGpuBigUIntExpression Right
    {
        get;
    }

    public long Bits
    {
        get;
    }

    public int NumWords => BigIntWidths.Words(Bits);

    protected BinaryExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right, Func<long, long, long> bound)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));

        // The children are immutable and know their bounds already, so the bound is computed once, here
        Bits = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));
    }

    public abstract ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}

internal class AddExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Add)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitAdd(this);
}

internal class SubExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.UnsignedSubtract)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitSub(this);
}

internal class MulExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Multiply)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitMul(this);
}

internal class DivExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Divide)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitDiv(this);
}

internal class BitwiseAndExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.And)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseAnd(this);
}

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits)
    : IGpuBigUIntExpression, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    internal int ShiftBits { get; } = shiftBits;
    public abstract long Bits { get; }
    public int NumWords => BigIntWidths.Words(Bits);

    public abstract ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
}

// a << k: a + k bits; a negative count shifts right, as BigInteger
internal class ShiftLeftExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftLeft(IBitBoundedExpression.Of(target), shiftBits);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitShiftLeft(this);
}

// a >> k: max(a - k, 1) bits; a negative count shifts left, as BigInteger
internal class ShiftRightExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftRight(IBitBoundedExpression.Of(target), shiftBits);

    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitShiftRight(this);
}

internal class SqrtExpression(IGpuBigUIntExpression target) : IGpuBigUIntExpression, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public long Bits { get; } = BigIntWidths.Sqrt(IBitBoundedExpression.Of(target));
    public int NumWords => BigIntWidths.Words(Bits);

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitSqrt(this);
}

internal class ModExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Remainder)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitMod(this);
}

internal class BitwiseOrExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Or)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseOr(this);
}

internal class BitwiseXorExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Xor)
{
    public override ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseXor(this);
}

// ~a: every bit of the words of a is inverted; the width of a defines the value
internal class BitwiseNotExpression(IGpuBigUIntExpression left) : IGpuBigUIntExpression, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Target { get; } = left ?? throw new ArgumentNullException(nameof(left));
    public long Bits { get; } = BigIntWidths.UnsignedNot(IBitBoundedExpression.Of(left));
    public int NumWords => BigIntWidths.Words(Bits);

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitBitwiseNot(this);
}

// a.Truncate(words): a mod 2^(32 words), as a C# unchecked narrowing; a.Fit(words): the same value, but a value that
// does not fit is a domain error, as a C# checked narrowing
internal class NarrowExpression(IGpuBigUIntExpression target, int words, bool isChecked)
    : IGpuBigUIntExpression, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    internal int Words { get; } = words;
    internal bool IsChecked { get; } = isChecked;
    public long Bits => BigIntWidths.OfWords(Words);
    public int NumWords => Words;

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitNarrow(this);
}
