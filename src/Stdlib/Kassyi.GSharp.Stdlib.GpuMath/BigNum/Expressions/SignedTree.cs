namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Walks of a signed expression tree with a stack on the heap instead of the call stack. An expression built in a loop
// (e = x + e, or a left fold) is as deep as the loop along either operand; a walk that recursed once per level would
// end the process with a stack overflow. The evaluation is the walk of ExpressionWalk, shared with the unsigned tree.
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
        var walker = new Walker(workspace);
        return ExpressionWalk.Evaluate<Walker, IGpuBigIntExpression, SignedValue, GpuBigUInt?>(ref walker, root,
            targetResult);
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

    // The signed tree for ExpressionWalk: a caller's target narrower than a node is rejected before any operand is
    // evaluated; a node that passes its target on (a negation) leaves the check to its operand
    private readonly struct Walker(GpuWorkspace workspace)
        : IExpressionWalker<IGpuBigIntExpression, SignedValue, GpuBigUInt?>
    {
        public int OperandCount(IGpuBigIntExpression node) => node is ISignedOperator op ? op.OperandCount : 0;

        public IGpuBigIntExpression GetOperand(IGpuBigIntExpression node, int index) =>
            ((ISignedOperator)node).GetOperand(index);

        public GpuBigUInt? OperandTarget(IGpuBigIntExpression node, GpuBigUInt? target) =>
            ((ISignedOperator)node).PassesTargetToOperand ? target : null;

        public void Enter(IGpuBigIntExpression node, GpuBigUInt? target)
        {
            if (node is ISignedOperator { PassesTargetToOperand: false })
            {
                SignedRoot.ThrowIfNarrower(target, node.NumWords);
            }
        }

        // A leaf, or a node implemented elsewhere, evaluates itself
        public SignedValue EvaluateLeaf(IGpuBigIntExpression node, GpuBigUInt? target) =>
            SignedValue.FromHost(node.Evaluate(workspace, target));

        public SignedValue Apply(IGpuBigIntExpression node, SignedValue first, SignedValue second, GpuBigUInt? target) =>
            ((ISignedOperator)node).Apply(workspace, first, second, target);

        public void Release(SignedValue value) => value.Dispose();
    }
}
