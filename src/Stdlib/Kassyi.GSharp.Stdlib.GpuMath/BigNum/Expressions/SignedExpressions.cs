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

// An operator node of this file: its operands are evaluated by SignedTree, which walks the tree without recursion
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
                ? SignedRoot.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first.Magnitude, second.Magnitude);
            var buffer = staging.Handle.IsDefault ? result : staging;
            if (plan.IsHost)
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
    private static void Release(SignedValue leftResult, SignedValue rightResult, GpuBigUInt staging,
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

// Walks of a signed expression tree with a stack on the heap instead of the call stack. An expression built in a loop
// (e = x + e, or a left fold) is as deep as the loop along either operand; a walk that recursed once per level would
// end the process with a stack overflow.
internal static class SignedTree
{
    // The node evaluated into the target, or into a rented buffer. Operands are evaluated before the node that uses
    // them, the left one first, as a recursive evaluation does; a caller's target is checked against the width before
    // anything is evaluated.
    public static GpuBigInt Evaluate(ISignedOperator root, GpuWorkspace workspace, GpuBigUInt? targetResult)
    {
        // The root's sign, when a kernel wrote it, is read once here (the only read of the evaluation)
        var value = EvaluateSigned(root, workspace, targetResult);
        try
        {
            return new GpuBigInt(value.Magnitude, SignedDeviceOps.ReadNegative(workspace, value.Sign));
        }
        catch
        {
            value.Magnitude.Dispose();
            throw;
        }
        finally
        {
            value.Sign.Dispose();
        }
    }

    // The value of the root with its sign as planned: a device sign stays on the device (a CUDA graph capture records
    // the kernels that write it, and the graph reads it after each launch)
    public static SignedValue EvaluateSigned(ISignedOperator root, GpuWorkspace workspace, GpuBigUInt? targetResult)
    {
        var frames = new PooledStack<Frame>();
        var values = new PooledStack<SignedValue>();
        try
        {
            Push(ref frames, root, targetResult);
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (frame.Node is not ISignedOperator node)
                {
                    // A leaf, or a node implemented elsewhere, evaluates itself
                    frames.Pop();
                    values.Push(SignedValue.FromHost(frame.Node.Evaluate(workspace, frame.Target)));
                    continue;
                }

                if (frame.Next < node.OperandCount)
                {
                    frames.Peek().Next = frame.Next + 1;
                    var operandTarget = node.PassesTargetToOperand ? frame.Target : null;
                    Push(ref frames, node.GetOperand(frame.Next), operandTarget);
                    continue;
                }

                // The operands go to Apply, which releases them
                frames.Pop();
                var second = node.OperandCount == 2 ? values.Pop() : default;
                var first = values.Pop();
                values.Push(node.Apply(workspace, first, second, frame.Target));
            }

            return values.Pop();
        }
        catch (Exception failure)
        {
            ReleaseAll(ref values, failure);
            throw;
        }
        finally
        {
            frames.Dispose();
            values.Dispose();
        }
    }

    // Disposes every node below root that holds resources (a node implemented elsewhere, such as a vector reduction);
    // the operator nodes of this file hold none. A node that throws does not stop the others; the exceptions are thrown
    // together at the end.
    public static void Dispose(ISignedOperator root)
    {
        var pending = new PooledStack<IGpuBigIntExpression>();
        List<Exception>? errors = null;
        try
        {
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                DisposeOrExpand(node, ref pending, ref errors);
            }
        }
        finally
        {
            pending.Dispose();
        }

        if (errors is not null)
        {
            if (errors.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }

            throw new AggregateException(errors);
        }
    }

    // An operator node pushes its operands (right to left, so that the left operand is disposed first, as a recursive
    // walk did); any other node is disposed, and an exception it throws is collected instead of stopping the walk
    private static void DisposeOrExpand(IGpuBigIntExpression node, ref PooledStack<IGpuBigIntExpression> pending,
        ref List<Exception>? errors)
    {
        if (node is ISignedOperator op)
        {
            for (var i = op.OperandCount - 1; i >= 0; i--)
            {
                pending.Push(op.GetOperand(i));
            }

            return;
        }

        try
        {
            (node as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
    }

    // An operand of a host sign plan with its host sign (a device sign, which such a plan does not depend on, as +)
    public static GpuBigInt WithHostSign(SignedValue value) =>
        new(value.Magnitude, !value.Sign.IsDevice && value.Sign.HostNegative);

    // Every node of the tree below root, root included
    public static void Collect(IGpuBigIntExpression root, HashSet<IGpuBigIntExpression> nodes)
    {
        var pending = new PooledStack<IGpuBigIntExpression>();
        try
        {
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!nodes.Add(node) || node is not ISignedOperator op)
                {
                    continue;
                }

                for (var i = 0; i < op.OperandCount; i++)
                {
                    pending.Push(op.GetOperand(i));
                }
            }
        }
        finally
        {
            pending.Dispose();
        }
    }

    // A caller's target narrower than the node is rejected before any operand is evaluated; a node that passes its
    // target on leaves the check to its operand
    private static void Push(ref PooledStack<Frame> frames, IGpuBigIntExpression node, GpuBigUInt? target)
    {
        if (node is ISignedOperator { PassesTargetToOperand: false })
        {
            SignedRoot.ThrowIfNarrower(target, node.NumWords);
        }

        frames.Push(new Frame(node, target));
    }

    // Releases the values evaluated before a failure. A release that throws does not stop the others; its exception is
    // thrown together with the failure.
    private static void ReleaseAll(ref PooledStack<SignedValue> values, Exception failure)
    {
        List<Exception>? errors = null;
        while (values.Count > 0)
        {
            try
            {
                values.Pop().Dispose();
            }
            catch (Exception ex)
            {
                (errors ??= [failure]).Add(ex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException(errors);
        }
    }

    // A node to evaluate, the target it writes into, and the index of the next operand to evaluate
    private struct Frame(IGpuBigIntExpression node, GpuBigUInt? target)
    {
        public readonly IGpuBigIntExpression Node = node;
        public readonly GpuBigUInt? Target = target;
        public int Next;
    }

    // A stack in an array rented from ArrayPool, grown by doubling
    private struct PooledStack<T>()
    {
        private T[] _items = ArrayPool<T>.Shared.Rent(16);

        public int Count { get; private set; }

        public ref T Peek() => ref _items[Count - 1];

        public void Push(T item)
        {
            if (Count == _items.Length)
            {
                var grown = ArrayPool<T>.Shared.Rent(_items.Length * 2);
                Array.Copy(_items, grown, Count);
                ArrayPool<T>.Shared.Return(_items, clearArray: true);
                _items = grown;
            }

            _items[Count++] = item;
        }

        public T Pop()
        {
            var item = _items[--Count];
            Array.Clear(_items, Count, 1); // T may hold references
            return item;
        }

        public void Dispose()
        {
            ArrayPool<T>.Shared.Return(_items, clearArray: true);
            _items = [];
            Count = 0;
        }
    }
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
    public static GpuBigUInt StagingFor(GpuWorkspace workspace, GpuBigUInt? target, int numWords, GpuBigUInt operand0,
        GpuBigUInt operand1)
    {
        if (target is not { } t ||
            (!DeviceRanges.Overlaps(t.Handle, operand0.Handle) &&
             !DeviceRanges.Overlaps(t.Handle, operand1.Handle)))
        {
            return default;
        }

        return workspace.RentTemp(numWords);
    }

    // The value in the buffer the caller sees: copied from the temporary when one was used; a caller's target gets its
    // words above the result cleared and is returned whole
    public static GpuBigUInt Deliver(GpuBigUInt staging, GpuBigUInt result, GpuBigUInt? target)
    {
        if (!staging.Handle.IsDefault)
        {
            result.CopyFrom(staging);
        }

        if (target is not { } t)
        {
            return result;
        }

        ClearAbove(t, result.NumWords);
        return t;
    }
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
        SignedCapture.ThrowIfCapturing(resultBuffer.Workspace, SignedCapture.DivisionReads);
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
        SignedCapture.ThrowIfCapturing(workspace, SignedCapture.DivisionReads);
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
                ? SignedRoot.Head(targetResult.Value, NumWords)
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
            // An owned intermediate goes back to the pool; a borrowed leaf result is left alone. Nested, so that a
            // release that throws does not skip the others.
            try
            {
                sign.Dispose();
            }
            finally
            {
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
        SignedRoot.ClearAbove(resultBuffer, copied);
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
        SignedDeviceOps.Select(a, b, max: true, resultBuffer);
}

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, Math.Max)
{
    public override SignedOperation Operation => SignedOperation.Min;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        throw SignedSelect.NotOnTheHost();

    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.Select(a, b, max: false, resultBuffer);
}

// Max and Min compare the operands on the device (SignedDeviceOps.Select); the plan never puts them on the host
internal static class SignedSelect
{
    public static InvalidOperationException NotOnTheHost() =>
        new("Max and Min choose their operand on the device; their sign plan is never a host plan.");
}

// The nodes whose result depends on a value read from the device refuse a CUDA graph capture before the read: a capture
// cannot record a host read (the read would invalidate it with a driver error), and the graph would replay the choice
// made for the values of the capture.
internal static class SignedCapture
{
    // DivRem reads the bit lengths of its operands (and % the zero test of its remainder)
    public const string DivisionReads = "The size of a quotient or remainder";

    public static void ThrowIfCapturing(GpuWorkspace workspace, string what)
    {
        if (DeviceCounted.IsCapturing(workspace))
        {
            throw new NotSupportedException(
                what + " is read from the device, which a CUDA graph capture cannot record. Evaluate the expression " +
                "without a graph.");
        }
    }
}
