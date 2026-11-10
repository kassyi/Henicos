namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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

            // The value of the root; a device sign is read once, here
            return values.Pop().ToHost();
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

    // The operands with their host signs, for a node that computes with host signs. Releases both when one carries a
    // device sign, which such a node cannot use.
    public static (GpuBigInt First, GpuBigInt Second) HostOperands(SignedValue first, SignedValue second)
    {
        if (first.Sign.IsDevice || second.Sign.IsDevice)
        {
            try
            {
                first.Dispose();
            }
            finally
            {
                second.Dispose();
            }

            throw new InvalidOperationException("An operand's sign is on the device; this node needs a host sign.");
        }

        return (first.ToHost(), second.ToHost());
    }

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
