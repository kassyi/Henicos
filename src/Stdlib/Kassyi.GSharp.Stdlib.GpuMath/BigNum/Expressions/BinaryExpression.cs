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
internal abstract class BinaryExpression : UnsignedOperatorExpression
{
    protected BinaryExpression(IGpuBigUIntExpression left, IGpuBigUIntExpression right, Func<long, long, long> bound)
        : base(
            left ?? throw new ArgumentNullException(nameof(left)),
            right ?? throw new ArgumentNullException(nameof(right))
        )
    {
        // The children are immutable and know their bounds already, so the bound is computed once, here
        Bits = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));
    }

    internal IGpuBigUIntExpression Left => FirstOperand;

    internal IGpuBigUIntExpression Right => SecondOperand;

    /// <inheritdoc/>
    public override long Bits { get; }
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

internal abstract class ShiftExpression(IGpuBigUIntExpression target, int shiftBits)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
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

internal class SqrtExpression(IGpuBigUIntExpression target)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
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
internal class BitwiseNotExpression(IGpuBigUIntExpression left)
    : UnsignedOperatorExpression(left ?? throw new ArgumentNullException(nameof(left)))
{
    public override long Bits { get; } = BigIntWidths.UnsignedNot(IBitBoundedExpression.Of(left));

    public override void Apply(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle first,
        GpuBigIntMemoryHandle second, GpuBigIntMemoryHandle result) =>
        context.Arithmetic.BitwiseNot(ref context, first, result);
}

// a.Truncate(words): a mod 2^(32 words), as a C# unchecked narrowing; a.Fit(words): the same value, but a value that
// does not fit is a domain error, as a C# checked narrowing
internal class NarrowExpression(IGpuBigUIntExpression target, int words, bool isChecked)
    : UnsignedOperatorExpression(target ?? throw new ArgumentNullException(nameof(target)))
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
