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

/// <summary>
/// A signed operator node: its operands are evaluated by <see cref="SignedTree"/>, which walks the tree without
/// recursion.
/// </summary>
internal interface ISignedOperator : IGpuBigIntExpression
{
    /// <summary>Gets the number of operands, 1 or 2.</summary>
    int OperandCount { get; }

    /// <summary>Gets an operand.</summary>
    /// <param name="index">0 for the first operand, 1 for the second.</param>
    /// <returns>The operand.</returns>
    IGpuBigIntExpression GetOperand(int index);

    /// <summary>Gets a value indicating whether the operand is evaluated into the node's own target (a negation only
    /// flips the sign of its operand).</summary>
    bool PassesTargetToOperand { get; }

    /// <summary>Gets the operation, for the sign planner (<see cref="SignPlanner"/>).</summary>
    SignedOperation Operation { get; }

    /// <summary>
    /// Applies the node to its evaluated operands, which the call owns and releases, also when it fails. The target,
    /// when given, is at least <see cref="IGpuBigIntExpression.NumWords"/> wide (checked before the operands were
    /// evaluated).
    /// </summary>
    /// <param name="workspace">The workspace of the evaluation.</param>
    /// <param name="first">The value of the first operand.</param>
    /// <param name="second">The value of the second operand; the default value for a unary node.</param>
    /// <param name="targetResult">The target, or <see langword="null"/> for a rented result.</param>
    /// <returns>The value of the node.</returns>
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

    // The sign is planned first (SignPlanner): a host sign computes as before; a sign that depends on the values, or on
    // an operand's device sign, is written by a kernel into a device word, so nothing is read on the host here.
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var plan = SignPlanner.For(Operation, first.Sign, second.Sign);
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;
        SignSource sign = default;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first.Magnitude, second.Magnitude);
            var buffer = staging.Handle.IsDefault ? result : staging;
            if (plan.Chosen != ChosenOperand.None)
            {
                // Max / Min of different host signs: the plan names the operand, which is copied with its sign flag
                // and without a comparison
                SignedSelect.Copy(plan.Chosen == ChosenOperand.First ? first.Magnitude : second.Magnitude, buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else if (plan.IsHost)
            {
                Execute(SignedTree.WithHostSign(first), SignedTree.WithHostSign(second), buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else
            {
                sign = ExecuteDevice(workspace, first, second, buffer, plan);
            }

            var value = new SignedValue(SignedRoot.Deliver(staging, result, targetResult), sign);
            sign = default; // Handed to the value
            rented = null; // Evaluated successfully, do not return the result buffer
            return value;
        }
        finally
        {
            try
            {
                sign.Dispose();
            }
            finally
            {
                Release(first, second, staging, rented);
            }
        }
    }

    // The magnitude with operands taken as non-negative (it does not depend on the signs, and no sign is read), and the
    // sign from the planned kernel. Add / Subtract, Max / Min and ~ override it with a kernel that does both.
    protected virtual SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan)
    {
        Execute(new GpuBigInt(a.Magnitude, false), new GpuBigInt(b.Magnitude, false), resultBuffer);
        return plan.Kernel switch
        {
            SignKernel.Xor => SignedDeviceOps.Xor(workspace, a.Sign, b.Sign),
            SignKernel.RemainderSign => SignedDeviceOps.RemainderSign(resultBuffer, a.Sign),
            _ => throw new InvalidOperationException($"{GetType().Name} has no device sign kernel {plan.Kernel}.")
        };
    }

    // A child result is owned (a rented intermediate) or borrowed (a leaf); Dispose returns only the former. Nested, so
    // that a release that throws (a failed capture refuses the clear) does not skip the others.
    internal static void Release(SignedValue leftResult, SignedValue rightResult, GpuBigUInt staging,
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

// a + b and a - b. Same effective host signs add the magnitudes into max + 1 bits, which never overflows; different
// signs (or a device sign) go to one kernel that subtracts, negates on a borrow and writes the sign into a device word.
// A zero difference is +0.
internal abstract class SignedAddSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right, bool subtract)
    : SignedBinaryExpression(left, right, BigIntWidths.Add)
{
    // Same effective host signs (the host plan): the magnitudes add, nothing is read
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        BigIntWideOps.AddSubtract(a.Value, b.Value, resultBuffer, subtract: false);
        return new GpuBigInt(resultBuffer, a.IsNegative);
    }

    // Different signs, or a device sign: one kernel adds or subtracts by the effective signs and writes the sign
    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.AddSubtract(a, b, subtract, resultBuffer);
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

        // The magnitude only: the sign comes from the plan (+ for a non-negative dividend, RemainderSignKernel
        // otherwise), so nothing is read here
        return new GpuBigInt(resultBuffer, isNegative: false);
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

    // The magnitude of the operand with the sign flipped: a host sign on the host, a device sign by a one-thread XOR
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        if (!first.Sign.IsDevice)
        {
            return new SignedValue(first.Magnitude, SignSource.Host(!first.Sign.HostNegative));
        }

        try
        {
            return new SignedValue(first.Magnitude,
                SignedDeviceOps.Xor(workspace, first.Sign, SignSource.Host(true)));
        }
        finally
        {
            first.Sign.Dispose();
        }
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

    // As SignedBinaryExpression.Apply: the sign is planned, a host sign computes as before, a device sign is written by
    // a kernel
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var plan = SignPlanner.For(Operation, first.Sign);
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;
        SignSource sign = default;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // As in SignedBinaryExpression: a target that overlaps the operand gets the result through a temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first.Magnitude, default);
            var buffer = staging.Handle.IsDefault ? result : staging;
            if (plan.IsHost)
            {
                Execute(SignedTree.WithHostSign(first), buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else
            {
                sign = ExecuteDevice(workspace, first, buffer, plan);
            }

            var value = new SignedValue(SignedRoot.Deliver(staging, result, targetResult), sign);
            sign = default;
            rented = null;
            return value;
        }
        finally
        {
            try
            {
                sign.Dispose();
            }
            finally
            {
                SignedBinaryExpression.Release(first, default, staging, rented);
            }
        }
    }

    // A device sign plan: the magnitude with the operand taken as non-negative and the sign forwarded (Fit); ~
    // overrides it with its kernel
    protected virtual SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue operand, GpuBigUInt resultBuffer,
        SignPlan plan)
    {
        Execute(new GpuBigInt(operand.Magnitude, false), resultBuffer);
        return plan.Kernel == SignKernel.Forward
            ? SignedDeviceOps.Xor(workspace, operand.Sign, SignSource.Host(false))
            : throw new InvalidOperationException($"{GetType().Name} has no device sign kernel {plan.Kernel}.");
    }

    // The operation on the evaluated operand into resultBuffer, which has exactly NumWords words
    protected abstract GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer);

    // The bound of the operand, rejecting a null operand before reading it
    protected static long OperandBits(IGpuBigIntExpression target) =>
        IBitBoundedExpression.Of(target ?? throw new ArgumentNullException(nameof(target)));
}

// ~x == -x - 1, as BigInteger and C# int: -(|x| + 1) for a non-negative x, |x| - 1 for a negative one. The result is
// one bit wider than x (GMP mpz_com). For a negative x the kernel tells -0 (whose ~ is -1) apart on the device.
internal class SignedBitwiseNotExpression(IGpuBigIntExpression target)
    : SignedUnaryExpression(target, BigIntWidths.SignedNot(OperandBits(target)))
{
    public override SignedOperation Operation => SignedOperation.Not;

    // A non-negative host x (the host plan): -(|x| + 1); the result is wider than the bound of x, so the carry is kept
    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        GpuBigIntMath.Add(targetEval.Value, 1u, resultBuffer);
        return new GpuBigInt(resultBuffer, isNegative: true);
    }

    // A negative or device-signed x: one kernel computes |x| - 1 (or -1 for -0) or |x| + 1 and writes the sign
    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue operand, GpuBigUInt resultBuffer,
        SignPlan plan) =>
        SignedDeviceOps.Not(operand, resultBuffer);
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
        throw SignedSelect.NotOnTheHost();

    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.Select(a, b, max: true, resultBuffer, plan);
}

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, Math.Max)
{
    public override SignedOperation Operation => SignedOperation.Min;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        throw SignedSelect.NotOnTheHost();

    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.Select(a, b, max: false, resultBuffer, plan);
}
