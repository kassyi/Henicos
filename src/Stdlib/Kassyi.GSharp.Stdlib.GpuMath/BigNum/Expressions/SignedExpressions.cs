namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Signed expression nodes. Every node has a static upper bound of its result in bits (BigIntWidths), computed from
// the bounds of its children when the expression is built; its result buffer has the words of that bound, so no
// node drops a carry. A caller's target must be at least that wide; the words above the result are cleared.

internal class GpuBigIntLeafExpression(GpuBigInt value) : IGpuBigIntExpression, IBitBoundedExpression
{
    public int NumWords => value.Value.NumWords;

    // Every bit of the operand's words: the leaf does not read its value
    public long Bits => BigIntWidths.OfWords(Math.Max(1, NumWords));

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (!targetResult.HasValue)
        {
            // The leaf borrows its operand: the caller may dispose the result without freeing the operand
            return new GpuBigInt(value.Value.AsBorrowed(), value.IsNegative);
        }

        var target = targetResult.Value;
        var words = value.Value.NumWords;
        SignedRoot.ThrowIfNarrower(target, words);

        // The target may be the operand itself (d.Value = d): then there is nothing to copy, and cuMemcpyDtoD does not
        // promise a copy onto itself
        if (target.DevicePointer != value.Value.DevicePointer)
        {
            if (DeviceRanges.Overlaps(target.DevicePointer, words, value.Value.DevicePointer, words))
            {
                // A target that partly overlaps the leaf (two views of one buffer): cuMemcpyDtoD does not promise
                // overlapping copies, so the words go through a temporary
                using var staging = new GpuBigUInt(workspace, words);
                staging.CopyFrom(value.Value, numWords: words);
                target.CopyFrom(staging, numWords: words);
            }
            else
            {
                target.CopyFrom(value.Value, numWords: words);
            }
        }

        SignedRoot.ClearAbove(target, words);
        return new GpuBigInt(target, value.IsNegative);
    }
}

internal abstract class SignedBinaryExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : IGpuBigIntExpression, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal IGpuBigIntExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    public void Dispose()
    {
        try
        {
            (Left as IDisposable)?.Dispose();
        }
        finally
        {
            (Right as IDisposable)?.Dispose();
        }
    }

    // The bound, computed once: the children never change, and a left fold builds chains as deep as the sequence,
    // where reading the children's bounds again at every level (and at every Evaluate) would cost 2^depth steps.
    private long _bits = -1;

    public long Bits =>
        _bits >= 0 ? _bits : _bits = ComputeBits(IBitBoundedExpression.Of(Left), IBitBoundedExpression.Of(Right));

    public int NumWords => BigIntWidths.Words(Bits);

    // The bound of the result from the bounds of the operands (BigIntWidths)
    protected abstract long ComputeBits(long left, long right);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        SignedRoot.ThrowIfNarrower(targetResult, NumWords);
        return Combine(workspace, EvaluateLeft(workspace), targetResult);
    }

    // The left operand. A left fold builds a chain of binary nodes along Left as deep as the sequence; the chain is
    // evaluated bottom-up in a loop, so that its length does not become the depth of the call stack. The order is that
    // of the recursion: the innermost left operand first, then each right operand before the node that uses it.
    private GpuBigInt EvaluateLeft(GpuWorkspace workspace)
    {
        if (Left is not SignedBinaryExpression)
        {
            return Left.Evaluate(workspace);
        }

        var depth = 0;
        var node = Left;
        while (node is SignedBinaryExpression inner)
        {
            depth++;
            node = inner.Left;
        }

        var chain = ArrayPool<SignedBinaryExpression>.Shared.Rent(depth);
        try
        {
            var count = 0;
            node = Left;
            while (node is SignedBinaryExpression inner)
            {
                chain[count++] = inner;
                node = inner.Left;
            }

            var accumulated = node.Evaluate(workspace);
            for (var i = count - 1; i >= 0; i--)
            {
                accumulated = chain[i].Combine(workspace, accumulated, targetResult: null);
            }

            return accumulated;
        }
        finally
        {
            ArrayPool<SignedBinaryExpression>.Shared.Return(chain, clearArray: true);
        }
    }

    // This node applied to its evaluated left operand, which the call owns and releases (also when it fails), and to
    // its right operand
    private GpuBigInt Combine(GpuWorkspace workspace, GpuBigInt leftResult, GpuBigUInt? targetResult)
    {
        GpuBigInt rightResult = default;
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;

        try
        {
            rightResult = Right.Evaluate(workspace);
            var result = targetResult.HasValue
                ? SignedRoot.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, leftResult, rightResult);
            var value = Execute(leftResult, rightResult, staging.Handle.IsDefault ? result : staging);
            value = SignedRoot.Deliver(value, staging, result, targetResult);
            rented = null; // Evaluated successfully, do not return the result buffer
            return value;
        }
        finally
        {
            Release(leftResult, rightResult, staging, rented);
        }
    }

    // A child result is owned (a rented intermediate) or borrowed (a leaf); Dispose returns only the former. Nested, so
    // that a release that throws (a failed capture refuses the clear) does not skip the others.
    private static void Release(GpuBigInt leftResult, GpuBigInt rightResult, GpuBigUInt staging,
        GpuBigUInt? unusedResultBuffer)
    {
        try
        {
            leftResult.Dispose();
        }
        finally
        {
            try
            {
                rightResult.Dispose();
            }
            finally
            {
                try
                {
                    staging.Dispose();
                }
                finally
                {
                    unusedResultBuffer?.Dispose();
                }
            }
        }
    }

    // a op b into resultBuffer, which has exactly NumWords words
    protected abstract GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer);
}

// The result buffer of a signed root: the first NumWords words of the caller's target, through a temporary when the
// target overlaps an operand, with the target's words above the result cleared
internal static class SignedRoot
{
    // A caller's target narrower than the bound would lose words
    public static void ThrowIfNarrower(GpuBigUInt? target, int numWords)
    {
        if (target is { } t && t.NumWords < numWords)
        {
            throw new ArgumentException(
                $"Destination buffer must have at least {numWords} words; it has {t.NumWords}. The result of an " +
                "expression is as wide as its bound; narrow it explicitly with Fit.", nameof(target));
        }
    }

    // The first numWords words of target, borrowed
    public static GpuBigUInt Head(GpuBigUInt target, int numWords) =>
        numWords == target.NumWords ? target : new GpuBigUInt(target.Workspace, target.Handle.Slice(0, numWords));

    // Clears the words of target above the first numWords (a memset, which a CUDA graph capture records)
    public static void ClearAbove(GpuBigUInt target, int numWords)
    {
        if (target.NumWords > numWords)
        {
            target.Workspace.Provider.ClearMemoryAsync(IntPtr.Add(target.DevicePointer, numWords * sizeof(uint)),
                target.NumWords - numWords, target.Workspace.Stream);
        }
    }

    // An owned temporary of numWords words when the target overlaps an operand; otherwise the default value, which owns
    // nothing
    public static GpuBigUInt StagingFor(GpuWorkspace workspace, GpuBigUInt? target, int numWords, GpuBigInt operand0,
        GpuBigInt operand1)
    {
        if (target is not { } t ||
            (!DeviceRanges.Overlaps(t.Handle, operand0.Value.Handle) &&
             !DeviceRanges.Overlaps(t.Handle, operand1.Value.Handle)))
        {
            return default;
        }

        return workspace.RentTemp(numWords);
    }

    // The value in the buffer the caller sees: copied from the temporary when one was used; a caller's target gets its
    // words above the result cleared and is returned whole
    public static GpuBigInt Deliver(GpuBigInt value, GpuBigUInt staging, GpuBigUInt result, GpuBigUInt? target)
    {
        if (!staging.Handle.IsDefault)
        {
            result.CopyFrom(staging);
        }

        if (target is not { } t)
        {
            return new GpuBigInt(result, value.IsNegative);
        }

        ClearAbove(t, result.NumWords);
        return new GpuBigInt(t, value.IsNegative);
    }
}

// a + b and a - b. Same effective signs add the magnitudes into max + 1 bits, which never overflows; different signs
// subtract them in one kernel that also negates on a borrow, and read the borrow and zero flags once (the sign of the
// result depends on the values). A zero result is +0.
internal abstract class SignedAddSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right, bool subtract)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => BigIntWidths.Add(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        var bNegative = b.IsNegative ^ subtract;
        if (a.IsNegative == bNegative)
        {
            BigIntWideOps.AddSubtract(a.Value, b.Value, resultBuffer, subtract: false);
            return new GpuBigInt(resultBuffer, a.IsNegative);
        }

        var (negated, isZero) = BigIntWideOps.SubtractMagnitudes(a.Value, b.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, !isZero && (negated ? bNegative : a.IsNegative));
    }
}

internal sealed class SignedAddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: false);

internal sealed class SignedSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: true);

internal class SignedMulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => BigIntWidths.Multiply(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        // The product kernels write the sum of the operand words; the bound may need fewer (a node narrower than its
        // words), and then the product goes through a temporary whose top words are zero
        var productWords = a.Value.NumWords + b.Value.NumWords;
        if (resultBuffer.NumWords == productWords)
        {
            GpuBigIntMath.Mul(a.Value, b.Value, resultBuffer);
        }
        else
        {
            using var product = resultBuffer.Workspace.RentTemp(productWords);
            GpuBigIntMath.Mul(a.Value, b.Value, product);
            resultBuffer.CopyFrom(product, numWords: resultBuffer.NumWords);
        }

        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedDivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => BigIntWidths.Divide(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        using var remainder = resultBuffer.Workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, resultBuffer, remainder);
        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedModExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => BigIntWidths.Remainder(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        // The remainder is below |b| and at most |a|, so the low words of the divisor-wide remainder hold it
        var workspace = resultBuffer.Workspace;
        using var quotient = workspace.RentTemp(a.Value.NumWords);
        using var remainder = workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, quotient, remainder);
        resultBuffer.CopyFrom(remainder, numWords: resultBuffer.NumWords);
        return new GpuBigInt(resultBuffer, a.IsNegative && !resultBuffer.IsZero());
    }
}

internal class SignedNegateExpression(IGpuBigIntExpression target)
    : IGpuBigIntExpression, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;
    public long Bits => IBitBoundedExpression.Of(Target);

    public void Dispose() => (Target as IDisposable)?.Dispose();

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var innerResult = Target.Evaluate(workspace, targetResult);
        return new GpuBigInt(innerResult.Value, !innerResult.IsNegative);
    }
}

internal abstract class SignedUnaryExpression(IGpuBigIntExpression target)
    : IGpuBigIntExpression, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public virtual long Bits => IBitBoundedExpression.Of(Target);
    public int NumWords => BigIntWidths.Words(Bits);

    public void Dispose() => (Target as IDisposable)?.Dispose();

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        SignedRoot.ThrowIfNarrower(targetResult, NumWords);

        GpuBigInt targetEval = default;
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;

        try
        {
            targetEval = Target.Evaluate(workspace);
            var result = targetResult.HasValue
                ? SignedRoot.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // As in SignedBinaryExpression: a target that overlaps the operand gets the result through a temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, targetEval, default);
            var value = Execute(targetEval, staging.Handle.IsDefault ? result : staging);
            value = SignedRoot.Deliver(value, staging, result, targetResult);
            rented = null;
            return value;
        }
        finally
        {
            // An owned intermediate goes back to the pool; a borrowed leaf result is left alone. Nested, so that a
            // release that throws does not skip the others.
            try
            {
                targetEval.Dispose();
            }
            finally
            {
                try
                {
                    staging.Dispose();
                }
                finally
                {
                    rented?.Dispose();
                }
            }
        }
    }

    // The operation on the evaluated operand into resultBuffer, which has exactly NumWords words
    protected abstract GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer);
}

internal class SignedBitwiseNotExpression(IGpuBigIntExpression target) : SignedUnaryExpression(target)
{
    // Every bit of the operand's words: the magnitude is inverted within them
    public override long Bits => BigIntWidths.OfWords(Math.Max(1, Target.NumWords));

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        GpuBigIntMath.BitwiseNot(targetEval.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}

// x.Fit(words): the value in words words, checked: a magnitude that does not fit is a domain error (C# checked
// narrowing). The sign is kept.
internal sealed class SignedFitExpression(IGpuBigIntExpression target, int words) : SignedUnaryExpression(target)
{
    public override long Bits => BigIntWidths.OfWords(words);

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        var magnitude = targetEval.Value;
        BigIntWideOps.CheckFits(magnitude, words);
        var copied = Math.Min(words, magnitude.NumWords);
        resultBuffer.CopyFrom(magnitude, numWords: copied);
        SignedRoot.ClearAbove(resultBuffer, copied);
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}

internal class SignedMaxExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => Math.Max(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        SignedSelect.Copy(a.CompareTo(b) >= 0 ? a : b, resultBuffer);
}

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override long ComputeBits(long left, long right) => Math.Max(left, right);

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        SignedSelect.Copy(a.CompareTo(b) <= 0 ? a : b, resultBuffer);
}

internal static class SignedSelect
{
    // The chosen operand in resultBuffer, which is at least as wide as either operand's words; the words above it are
    // cleared
    public static GpuBigInt Copy(GpuBigInt chosen, GpuBigUInt resultBuffer)
    {
        var words = chosen.Value.NumWords;
        resultBuffer.CopyFrom(chosen.Value, numWords: words);
        SignedRoot.ClearAbove(resultBuffer, words);
        return new GpuBigInt(resultBuffer, chosen.IsNegative);
    }
}
