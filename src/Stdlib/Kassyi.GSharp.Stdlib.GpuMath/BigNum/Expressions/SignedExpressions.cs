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

        ResultBuffers.ClearAbove(target, words);
        return new GpuBigInt(target, value.IsNegative);
    }
}

// A signed operator node: its operands are evaluated by SignedTree, which walks the tree without recursion
internal interface ISignedOperator : IGpuBigIntExpression
{
    // 1 or 2
    int OperandCount { get; }

    IGpuBigIntExpression GetOperand(int index);

    // Whether the operand is evaluated into the node's own target (a negation only flips the sign of its operand)
    bool PassesTargetToOperand { get; }

    // The operation, for the sign planner (SignPlanner)
    SignedOperation Operation { get; }

    // The node applied to its evaluated operands, which the call owns and releases (also when it fails); second is the
    // default value for a unary node. The target, when given, is at least NumWords wide (checked before the operands
    // were evaluated).
    SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult);
}

// The bound of every node is computed when the node is built, from the bounds its operands computed when they were
// built: reading it never walks the tree, however deep a loop made it.
internal abstract class SignedBinaryExpression(
    IGpuBigIntExpression left,
    IGpuBigIntExpression right,
    Func<long, long, long> bound
) : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal IGpuBigIntExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    // After Left and Right, which reject a null operand first
    public long Bits { get; } = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 2;

    public bool PassesTargetToOperand => false;

    public IGpuBigIntExpression GetOperand(int index) => index == 0 ? Left : Right;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public abstract SignedOperation Operation { get; }

    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var (a, b) = SignedTree.HostOperands(first, second);
        return SignedValue.FromHost(ApplyHost(workspace, a, b, targetResult));
    }

    private GpuBigInt ApplyHost(GpuWorkspace workspace, GpuBigInt first, GpuBigInt second, GpuBigUInt? targetResult)
    {
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first, second);
            var value = Execute(first, second, staging.Handle.IsDefault ? result : staging);
            value = SignedRoot.Deliver(value, staging, result, targetResult);
            rented = null; // Evaluated successfully, do not return the result buffer
            return value;
        }
        finally
        {
            Release(first, second, staging, rented);
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

// a + b and a - b. Same effective signs add the magnitudes into max + 1 bits, which never overflows; different signs
// subtract them in one kernel that also negates on a borrow, and read the borrow and zero flags once (the sign of the
// result depends on the values). A zero result is +0.
internal abstract class SignedAddSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right, bool subtract)
    : SignedBinaryExpression(left, right, BigIntWidths.Add)
{
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        var negative = BigIntWideOps.AddSigned(a.Value, a.IsNegative, b.Value, b.IsNegative ^ subtract, resultBuffer);
        return new GpuBigInt(resultBuffer, negative);
    }
}

internal sealed class SignedAddExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: false)
{
    public override SignedOperation Operation => SignedOperation.Add;
}

internal sealed class SignedSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedAddSubExpression(left, right, subtract: true)
{
    public override SignedOperation Operation => SignedOperation.Subtract;
}

internal class SignedMulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, BigIntWidths.Multiply)
{
    public override SignedOperation Operation => SignedOperation.Multiply;

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
    : SignedBinaryExpression(left, right, BigIntWidths.Divide)
{
    public override SignedOperation Operation => SignedOperation.Divide;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        CaptureChecks.ThrowIfCapturing(resultBuffer.Workspace, CaptureChecks.DivisionReads);
        using var remainder = resultBuffer.Workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, resultBuffer, remainder);
        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}

internal class SignedModExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, BigIntWidths.Remainder)
{
    public override SignedOperation Operation => SignedOperation.Remainder;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        // The remainder is below |b| and at most |a|, so the low words of the divisor-wide remainder hold it
        var workspace = resultBuffer.Workspace;
        CaptureChecks.ThrowIfCapturing(workspace, CaptureChecks.DivisionReads);
        using var quotient = workspace.RentTemp(a.Value.NumWords);
        using var remainder = workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, quotient, remainder);
        resultBuffer.CopyFrom(remainder, numWords: resultBuffer.NumWords);
        return new GpuBigInt(resultBuffer, a.IsNegative && !resultBuffer.IsZero());
    }
}

// -x: the operand evaluated into the node's own target, with the sign flipped
internal class SignedNegateExpression(IGpuBigIntExpression target) : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    public long Bits { get; } = IBitBoundedExpression.Of(target);

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 1;

    public bool PassesTargetToOperand => true;

    public IGpuBigIntExpression GetOperand(int index) => Target;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public SignedOperation Operation => SignedOperation.Negate;

    // The magnitude of the operand with the sign flipped; nothing is launched
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var (a, _) = SignedTree.HostOperands(first, second);
        return new SignedValue(a.Value, SignSource.Host(!a.IsNegative));
    }
}

// A unary operation into a buffer of its own. The bound is given by the derived node when it is built.
internal abstract class SignedUnaryExpression(IGpuBigIntExpression target, long bits)
    : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    public long Bits { get; } = bits;

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 1;

    public bool PassesTargetToOperand => false;

    public IGpuBigIntExpression GetOperand(int index) => Target;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public abstract SignedOperation Operation { get; }

    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var (a, b) = SignedTree.HostOperands(first, second);
        return SignedValue.FromHost(ApplyHost(workspace, a, b, targetResult));
    }

    private GpuBigInt ApplyHost(GpuWorkspace workspace, GpuBigInt first, GpuBigInt second, GpuBigUInt? targetResult)
    {
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // As in SignedBinaryExpression: a target that overlaps the operand gets the result through a temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first, default);
            var value = Execute(first, staging.Handle.IsDefault ? result : staging);
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
                first.Dispose();
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

    // The bound of the operand, rejecting a null operand before reading it
    protected static long OperandBits(IGpuBigIntExpression target) =>
        IBitBoundedExpression.Of(target ?? throw new ArgumentNullException(nameof(target)));
}

// ~x == -x - 1, as BigInteger and C# int: -(|x| + 1) for a non-negative x, |x| - 1 for a negative one. The result is
// one bit wider than x (GMP mpz_com). A negative x reads the borrow of |x| - 1 from the device, once: a magnitude of
// zero with the negative flag (-0) is 0, and ~0 is -1.
internal class SignedBitwiseNotExpression(IGpuBigIntExpression target)
    : SignedUnaryExpression(target, BigIntWidths.SignedNot(OperandBits(target)))
{
    public override SignedOperation Operation => SignedOperation.Not;

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        if (!targetEval.IsNegative)
        {
            // The result is wider than the bound of x, so the carry of |x| + 1 is kept
            GpuBigIntMath.Add(targetEval.Value, 1u, resultBuffer);
            return new GpuBigInt(resultBuffer, isNegative: true);
        }

        var workspace = resultBuffer.Workspace;
        using var one = workspace.RentTemp(1);
        workspace.Provider.WriteWord(one.DevicePointer, 0, 1u, workspace.Stream);
        var (negated, _) = BigIntWideOps.SubtractMagnitudes(targetEval.Value, one, resultBuffer);

        // |x| - 1 borrowed only for -0: the result is then |0 - 1| = 1 with a minus sign
        return new GpuBigInt(resultBuffer, isNegative: negated);
    }
}

// x.Fit(words): the value in words words, checked: a magnitude that does not fit is a domain error (C# checked
// narrowing). The sign is kept.
internal sealed class SignedFitExpression(IGpuBigIntExpression target, int words)
    : SignedUnaryExpression(target, BigIntWidths.OfWords(words))
{
    public override SignedOperation Operation => SignedOperation.Fit;

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        var magnitude = targetEval.Value;
        BigIntWideOps.CheckFits(magnitude, words);
        var copied = Math.Min(words, magnitude.NumWords);
        resultBuffer.CopyFrom(magnitude, numWords: copied);
        ResultBuffers.ClearAbove(resultBuffer, copied);
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}

internal class SignedMaxExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, Math.Max)
{
    public override SignedOperation Operation => SignedOperation.Max;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        SignedSelect.Copy(SignedSelect.Compare(a, b) >= 0 ? a : b, resultBuffer);
}

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, Math.Max)
{
    public override SignedOperation Operation => SignedOperation.Min;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        SignedSelect.Copy(SignedSelect.Compare(a, b) <= 0 ? a : b, resultBuffer);
}
