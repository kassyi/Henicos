namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A stack-scoped visitor that evaluates unsigned GPU BigInteger AST expressions using rented device memory buffers.
/// </summary>
/// <param name="context">The execution context providing memory manager and arithmetic providers.</param>
/// <param name="targetResult">
/// An optional pre-allocated destination buffer to receive the root evaluation result. A root node wider than it throws
/// <see cref="ArgumentException"/> before anything is written; the root writes the first words of a wider one.
/// </param>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Traverses the expression AST and emits low-level kernel invocations, managing intermediate temporary buffer lifetimes
/// to prevent device memory leaks and eliminate managed heap allocations.
/// </para>
/// <para>
/// <b>Depth:</b> The operator nodes of GpuMath are evaluated with a stack on the heap, not by recursion, so an
/// expression built in a loop can be as deep as the loop. A node implemented elsewhere evaluates itself through
/// <see cref="IGpuBigUIntExpression.Accept"/>; the operands it visits with <see cref="Visit"/> start a walk of their own.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a <see langword="ref struct"/>, the visitor lives solely on the call stack; the stacks of
/// the walk are rented from <see cref="ArrayPool{T}.Shared"/>.
/// </para>
/// </remarks>
public ref struct BigIntExpressionVisitor(GpuBigIntExecutionContext context, ScopedBigIntMemory targetResult = default)
    : IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>
{
    /// <summary>
    /// The execution context for memory allocation and kernel dispatch.
    /// </summary>
    public GpuBigIntExecutionContext Context = context;

    // An optional target buffer to use for the root evaluation
    private ScopedBigIntMemory _targetResult = targetResult;

    // Whether the node being entered is the root of the evaluation, the only node that may write into the target
    private bool _isRoot = true;

    /// <summary>
    /// Visits and evaluates an arbitrary expression AST node.
    /// </summary>
    /// <param name="node">The expression node to visit.</param>
    /// <returns>A scoped memory segment holding the evaluated result.</returns>
    public ScopedBigIntMemory Visit(IGpuBigUIntExpression node)
    {
        var prev = _isRoot;
        _isRoot = false;
        var res = node.Accept(ref this);
        _isRoot = prev;
        return res;
    }

    /// <summary>
    /// Visits a leaf constant GPU BigInteger and wraps it in a non-owning scoped memory handle.
    /// </summary>
    /// <param name="node">The GPU BigInteger node.</param>
    /// <returns>A scoped memory segment referencing the GPU BigInteger buffer.</returns>
    public ScopedBigIntMemory VisitGpuUIntBigInteger(GpuBigUInt node)
    {
        // Wrapper without transferring ownership
        return new ScopedBigIntMemory(node.Handle.Borrow());
    }

    // An operator node of GpuMath, and the operator nodes below it, evaluated by ExpressionWalk; the node is the root
    // when the visitor entered it as the root
    internal ScopedBigIntMemory Walk(IUnsignedOperator node)
    {
        var isRoot = _isRoot;
        _isRoot = false;
        try
        {
            return new ScopedBigIntMemory(
                ExpressionWalk.Evaluate<BigIntExpressionVisitor, IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>(
                    ref this,
                    node,
                    isRoot
                )
            );
        }
        finally
        {
            _isRoot = isRoot;
        }
    }

    // The root writes into the first numWords words of the target, unless the target overlaps one of the root's
    // operands: the kernels take their pointers as __restrict__ and some read words that other threads write, so the
    // root then computes into a temporary, which the caller copies into the target. Only the root can meet the target:
    // every other node writes a temporary, and the root reads only its own operands. A target narrower than the root
    // is rejected; the caller clears the words of a wider one above the result.
    private ScopedBigIntMemory GetResultBuffer(int numWords, bool isRoot, GpuBigIntMemoryHandle operand0,
        GpuBigIntMemoryHandle operand1 = default)
    {
        if (isRoot && !_targetResult.Handle.IsDefault)
        {
            if (_targetResult.Handle.NumWords < numWords)
            {
                throw new ArgumentException(
                    $"Destination buffer must have at least {numWords} words; it has " +
                    $"{_targetResult.Handle.NumWords}. Narrow the expression with Fit or Truncate.",
                    nameof(targetResult)
                );
            }

            var target = _targetResult;
            _targetResult = default;
            if (!DeviceRanges.Overlaps(target.Handle, operand0) && !DeviceRanges.Overlaps(target.Handle, operand1))
            {
                return new ScopedBigIntMemory(target.Handle.Slice(0, numWords));
            }
        }

        return Context.MemoryManager.AllocateTemp(numWords);
    }

    readonly int IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.OperandCount(
        IGpuBigUIntExpression node) =>
        node is IUnsignedOperator op ? op.OperandCount : 0;

    readonly IGpuBigUIntExpression IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.GetOperand(
        IGpuBigUIntExpression node, int index) =>
        ((IUnsignedOperator)node).GetOperand(index);

    // The operands of a node are never the root
    readonly bool IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.OperandTarget(
        IGpuBigUIntExpression node, bool isRoot) =>
        false;

    // The target is checked by the root, when it takes its result buffer
    readonly void IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.Enter(
        IGpuBigUIntExpression node, bool isRoot)
    {
    }

    // A leaf, or a node implemented elsewhere, evaluates itself; the value owns what the node's memory owned
    GpuBigIntMemoryHandle IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.EvaluateLeaf(
        IGpuBigUIntExpression node, bool isRoot)
    {
        _isRoot = isRoot;
        try
        {
            return node.Accept(ref this).Handle;
        }
        finally
        {
            _isRoot = false;
        }
    }

    // The result buffer is taken while the operands are alive; after the operation the right operand is returned
    // first, then the left, as the operands of a recursive evaluation went out of scope. A result buffer the operation
    // did not fill goes back as well.
    GpuBigIntMemoryHandle IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.Apply(
        IGpuBigUIntExpression node, GpuBigIntMemoryHandle first, GpuBigIntMemoryHandle second, bool isRoot)
    {
        var op = (IUnsignedOperator)node;
        ScopedBigIntMemory result = default;
        try
        {
            result = GetResultBuffer(op.NumWords, isRoot, first, second);
            op.Apply(ref Context, first, second, result.Handle);
            return result.Handle;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            try
            {
                Release(second);
            }
            finally
            {
                Release(first);
            }
        }
    }

    readonly void IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.Release(
        GpuBigIntMemoryHandle value) =>
        Release(value);

    // An owned temporary goes back to its pool; a borrowed leaf or a slice of the target owns nothing
    private static void Release(GpuBigIntMemoryHandle value)
    {
        var memory = new ScopedBigIntMemory(value);
        memory.Dispose();
    }
}
