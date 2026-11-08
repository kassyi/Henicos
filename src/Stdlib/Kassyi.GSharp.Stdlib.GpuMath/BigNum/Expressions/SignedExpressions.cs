namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class GpuBigIntLeafExpression(GpuBigInt value) : IGpuBigIntExpression
{
    public int NumWords => value.Value.NumWords;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (!targetResult.HasValue)
        {
            // The leaf borrows its operand: the caller may dispose the result without freeing the operand
            return new GpuBigInt(value.Value.AsBorrowed(), value.IsNegative);
        }

        // The target may be the operand itself (d.Value = d): then there is nothing to copy, and cuMemcpyDtoD does not
        // promise a copy onto itself
        var target = targetResult.Value;
        if (target.DevicePointer != value.Value.DevicePointer)
        {
            var words = Math.Min(target.NumWords, value.Value.NumWords);
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
                target.CopyFrom(value.Value);
            }
        }

        return new GpuBigInt(targetResult.Value, value.IsNegative);
    }
}

internal abstract class SignedBinaryExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : IGpuBigIntExpression, IDisposable
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

    // The width, computed once: the children never change, and a left fold builds chains as deep as the sequence,
    // where reading the children's widths again at every level (and at every Evaluate) would cost 2^depth steps.
    private int _numWords = -1;

    public int NumWords => _numWords >= 0 ? _numWords : _numWords = ComputeNumWords();

    protected virtual int ComputeNumWords()
    {
        var leftWords = Left.NumWords;
        if (leftWords != Right.NumWords)
        {
            throw new ArgumentException("Left and Right expressions must have the same number of words.");
        }

        return leftWords;
    }

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (targetResult.HasValue && targetResult.Value.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

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
        var finalBuffer = targetResult;

        try
        {
            rightResult = Right.Evaluate(workspace);
            finalBuffer ??= workspace.RentTemp(NumWords);

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary (design gpumath-m1-misc 4.2.5)
            staging = SignedRoot.StagingFor(workspace, targetResult, leftResult, rightResult);
            var result = Execute(leftResult, rightResult, staging.Handle.IsDefault ? finalBuffer.Value : staging);
            result = SignedRoot.Deliver(result, staging, finalBuffer.Value);
            finalBuffer = null; // Evaluated successfully, do not return the result buffer
            return result;
        }
        finally
        {
            Release(leftResult, rightResult, staging, targetResult.HasValue ? null : finalBuffer);
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

    protected abstract GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer);
}

// The overlap guard of the signed roots: a caller's target that overlaps an operand is not written by the kernel; the
// result goes to a temporary and is then copied into the target
internal static class SignedRoot
{
    // An owned temporary when the target overlaps an operand; otherwise the default value, which owns nothing
    public static GpuBigUInt StagingFor(GpuWorkspace workspace, GpuBigUInt? target, GpuBigInt operand0,
        GpuBigInt operand1)
    {
        if (target is not { } t ||
            (!DeviceRanges.Overlaps(t.Handle, operand0.Value.Handle) &&
             !DeviceRanges.Overlaps(t.Handle, operand1.Value.Handle)))
        {
            return default;
        }

        return workspace.RentTemp(t.NumWords);
    }

    // The result in the buffer the caller sees: copied from the temporary into the target when one was used
    public static GpuBigInt Deliver(GpuBigInt result, GpuBigUInt staging, GpuBigUInt target)
    {
        if (staging.Handle.IsDefault)
        {
            return result;
        }

        target.CopyFrom(staging);
        return new GpuBigInt(target, result.IsNegative);
    }
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
    protected override int ComputeNumWords() => Left.NumWords + Right.NumWords;

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

internal class SignedModExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        GpuBigIntDivRem.Mod(a.Value, b.Value, resultBuffer);
        return new GpuBigInt(resultBuffer, a.IsNegative && !resultBuffer.IsZero());
    }
}

internal class SignedNegateExpression(IGpuBigIntExpression target) : IGpuBigIntExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public int NumWords => Target.NumWords;

    public void Dispose() => (Target as IDisposable)?.Dispose();

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var innerResult = Target.Evaluate(workspace, targetResult);
        return new GpuBigInt(innerResult.Value, !innerResult.IsNegative);
    }
}

internal abstract class SignedUnaryExpression(IGpuBigIntExpression target) : IGpuBigIntExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public virtual int NumWords => Target.NumWords;

    public void Dispose() => (Target as IDisposable)?.Dispose();

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (targetResult.HasValue && targetResult.Value.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        GpuBigInt targetEval = default;
        GpuBigUInt staging = default;
        var finalBuffer = targetResult;

        try
        {
            targetEval = Target.Evaluate(workspace);
            finalBuffer ??= workspace.RentTemp(NumWords);

            // As in SignedBinaryExpression: a target that overlaps the operand gets the result through a temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, targetEval, default);
            var result = Execute(targetEval, staging.Handle.IsDefault ? finalBuffer.Value : staging);
            result = SignedRoot.Deliver(result, staging, finalBuffer.Value);
            finalBuffer = null;
            return result;
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
                    if (!targetResult.HasValue && finalBuffer.HasValue)
                    {
                        finalBuffer.Value.Dispose();
                    }
                }
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

internal class SignedMaxExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        var cmp = a.CompareTo(b);
        if (cmp >= 0)
        {
            resultBuffer.CopyFrom(a.Value);
            return new GpuBigInt(resultBuffer, a.IsNegative);
        }

        resultBuffer.CopyFrom(b.Value);
        return new GpuBigInt(resultBuffer, b.IsNegative);
    }
}

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        var cmp = a.CompareTo(b);
        if (cmp <= 0)
        {
            resultBuffer.CopyFrom(a.Value);
            return new GpuBigInt(resultBuffer, a.IsNegative);
        }

        resultBuffer.CopyFrom(b.Value);
        return new GpuBigInt(resultBuffer, b.IsNegative);
    }
}
