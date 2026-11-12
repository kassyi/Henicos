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
/// <b>Depth:</b> The visitor evaluates the first levels of operator nodes by recursion, the fastest form for an
/// expression of ordinary depth, and the levels below them with an explicit stack instead of the call stack, so an
/// expression built in a loop can be as deep as the loop. A node implemented elsewhere evaluates itself through
/// <see cref="IGpuBigUIntExpression.Accept"/>; the operands it visits with <see cref="Visit"/> are evaluated the same way.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a <see langword="ref struct"/>, the visitor lives solely on the call stack; the stacks of
/// the walk live in its frame, and only a deep walk rents an array from <see cref="ArrayPool{T}.Shared"/>.
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

    // The operator nodes being evaluated by recursion
    private int _depth;

    /// <summary>The number of levels of operator nodes evaluated by recursion before the walk takes over.</summary>
    internal const int DefaultRecursionLimit = 16;

    /// <summary>
    /// Gets or sets the number of levels of operator nodes evaluated by recursion; the levels below are walked with an
    /// explicit stack. Tests set 0 to walk the whole tree.
    /// </summary>
    internal int RecursionLimit { get; set; } = DefaultRecursionLimit;

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

    // An operator node of GpuMath: by recursion within the first RecursionLimit levels, otherwise by ExpressionWalk
    // with the operator nodes below it; the node is the root when the visitor entered it as the root
    internal ScopedBigIntMemory Walk(UnsignedOperatorExpression node)
    {
        if (_depth < RecursionLimit)
        {
            // A failure ends the evaluation; a depth left too high would only start the walk earlier
            _depth++;
            var value = Recurse(node);
            _depth--;
            return value;
        }

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

    // The node evaluated by recursion: its operands through Accept, the left one first, then the node into a result
    // buffer taken while the operands are alive; the right operand is returned first, then the left, and a result
    // buffer the operation did not fill goes back as well. ExpressionWalk keeps the same order.
    private ScopedBigIntMemory Recurse(UnsignedOperatorExpression node)
    {
        var isRoot = _isRoot;
        _isRoot = false;
        using var first = node.FirstOperand.Accept(ref this);
        using var second = node.OperandCount == 2 ? node.SecondOperand.Accept(ref this) : default;
        _isRoot = isRoot;
        var result = GetResultBuffer(node.NumWords, isRoot, first.Handle, second.Handle);
        try
        {
            node.Apply(ref Context, first.Handle, second.Handle, result.Handle);
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    // The root writes into the first numWords words of the target, unless the target overlaps one of the root's
    // operands: some kernels read words that other threads write, so the root then computes into a temporary, which
    // the caller copies into the target. Only the root can meet the target:
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

    /// <inheritdoc/>
    readonly int IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.OperandCount(
        IGpuBigUIntExpression node, bool isRoot) =>
        node is UnsignedOperatorExpression op ? op.OperandCount : 0;

    /// <inheritdoc/>
    readonly IGpuBigUIntExpression IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.GetOperand(
        IGpuBigUIntExpression node, int index) =>
        index == 0
            ? ((UnsignedOperatorExpression)node).FirstOperand
            : ((UnsignedOperatorExpression)node).SecondOperand;

    // The operands of a node are never the root
    /// <inheritdoc/>
    readonly bool IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.OperandTarget(
        IGpuBigUIntExpression node, bool isRoot) =>
        false;

    // The target is checked by the root, when it takes its result buffer
    /// <inheritdoc/>
    readonly void IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.Enter(
        IGpuBigUIntExpression node, bool isRoot)
    {
    }

    // A leaf, or a node implemented elsewhere, evaluates itself; the value owns what the node's memory owned
    /// <inheritdoc/>
    GpuBigIntMemoryHandle IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.EvaluateLeaf(
        IGpuBigUIntExpression node, bool isRoot)
    {
        // A failure ends the walk, and Walk restores the flag
        _isRoot = isRoot;
        var handle = node.Accept(ref this).Handle;
        _isRoot = false;
        return handle;
    }

    // The result buffer is taken while the operands are alive; after the operation the right operand is returned
    // first, then the left, as the operands of a recursive evaluation went out of scope. A result buffer the operation
    // did not fill goes back as well.
    /// <inheritdoc/>
    GpuBigIntMemoryHandle IExpressionWalker<IGpuBigUIntExpression, GpuBigIntMemoryHandle, bool>.Apply(
        IGpuBigUIntExpression node, GpuBigIntMemoryHandle first, GpuBigIntMemoryHandle second, bool isRoot)
    {
        var op = (UnsignedOperatorExpression)node;
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

    /// <inheritdoc/>
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
