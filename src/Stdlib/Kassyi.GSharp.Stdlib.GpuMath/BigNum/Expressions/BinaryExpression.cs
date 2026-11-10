namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Unsigned expression nodes. Every node has a static upper bound of its result in bits (BigIntWidths), computed from
// the bounds of its children when the node is built; its result has the words of that bound, so no node drops a carry,
// the top words of a product or the bits of a left shift. A difference below zero is a domain error.

// An unsigned operator node: BigIntExpressionVisitor evaluates its operands with ExpressionWalk, without recursion, and
// then applies the node
internal interface IUnsignedOperator : IGpuBigUIntExpression
{
    // 1 or 2
    int OperandCount { get; }

    IGpuBigUIntExpression GetOperand(int index);

    // The operation on the evaluated operands into result, which has NumWords words; second is the default handle for
    // a unary node
    void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first, GpuBigIntMemoryHandle second,
        GpuBigIntMemoryHandle result);
}

/// <summary>
/// Abstract base AST node representing a binary GPU BigInteger operation between two operand subtrees.
/// </summary>
/// <remarks>
/// The operands may have different widths; the result has the words of the node's bound.
/// </remarks>
internal abstract class BinaryExpression : IUnsignedOperator, IBitBoundedExpression
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

    public int OperandCount => 2;

    protected BinaryExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right, Func<long, long, long> bound)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));

        // The children are immutable and know their bounds already, so the bound is computed once, here
        Bits = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));
    }

    public IGpuBigUIntExpression GetOperand(int index) => index == 0 ? Left : Right;

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.Walk(this);

    public abstract void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result);
}

internal class AddExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Add)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Add(ref context, first, second, result);
}

internal class SubExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.UnsignedSubtract)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Sub(ref context, first, second, result);
}

internal class MulExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Multiply)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Mul(ref context, first, second, result);
}

internal class DivExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Divide)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Div(ref context, first, second, result);
}

internal class BitwiseAndExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.And)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseAnd(ref context, first, second, result);
}

// A node with one operand
internal abstract class UnaryExpression(IGpuBigUIntExpression target) : IUnsignedOperator, IBitBoundedExpression
{
    internal IGpuBigUIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    public abstract long Bits { get; }

    public virtual int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 1;

    public IGpuBigUIntExpression GetOperand(int index) => Target;

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.Walk(this);

    public abstract void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result);
}

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits) : UnaryExpression(target)
{
    internal int ShiftBits { get; } = shiftBits;
}

// a << k: a + k bits; a negative count shifts right, as BigInteger
internal class ShiftLeftExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftLeft(IBitBoundedExpression.Of(target), shiftBits);

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.ShiftLeft(ref context, first, ShiftBits, result);
}

// a >> k: max(a - k, 1) bits; a negative count shifts left, as BigInteger
internal class ShiftRightExpression(IGpuBigUIntExpression target, int shiftBits) : ShiftExpression(target, shiftBits)
{
    public override long Bits { get; } = BigIntWidths.ShiftRight(IBitBoundedExpression.Of(target), shiftBits);

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.ShiftRight(ref context, first, ShiftBits, result);
}

internal class SqrtExpression(IGpuBigUIntExpression target) : UnaryExpression(target)
{
    public override long Bits { get; } = BigIntWidths.Sqrt(IBitBoundedExpression.Of(target));

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Sqrt(ref context, first, result);
}

internal class ModExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Remainder)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.Mod(ref context, first, second, result);
}

internal class BitwiseOrExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Or)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseOr(ref context, first, second, result);
}

internal class BitwiseXorExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right)
    : BinaryExpression(left, right, BigIntWidths.Xor)
{
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseXor(ref context, first, second, result);
}

// ~a: every bit of the words of a is inverted; the width of a defines the value
internal class BitwiseNotExpression(IGpuBigUIntExpression left) : UnaryExpression(left)
{
    public override long Bits { get; } = BigIntWidths.UnsignedNot(IBitBoundedExpression.Of(left));

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseNot(ref context, first, result);
}

// a.Truncate(words): a mod 2^(32 words), as a C# unchecked narrowing; a.Fit(words): the same value, but a value that
// does not fit is a domain error, as a C# checked narrowing
internal class NarrowExpression(IGpuBigUIntExpression target, int words, bool isChecked) : UnaryExpression(target)
{
    internal int Words { get; } = words;
    internal bool IsChecked { get; } = isChecked;
    public override long Bits => BigIntWidths.OfWords(Words);
    public override int NumWords => Words;

    // The low words of the operand, zero-extended; Fit also checks that no word above them is set
    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result)
    {
        var workspace = context.GpuWorkspace;
        // Borrowed views: disposing them frees nothing
        using var operand = new GpuBigUInt(workspace, first.Borrow());
        if (IsChecked)
        {
            BigIntWideOps.CheckFits(operand, Words);
        }

        var copied = Math.Min(Words, operand.NumWords);
        using var resultBuffer = new GpuBigUInt(workspace, result.Borrow());
        resultBuffer.CopyFrom(operand, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
    }
}
