namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Evaluates and disposes vector expressions (<see cref="IVectorOperator"/>) without one call stack frame per level:
/// an expression built in a loop (<c>v = v.SelectAdd(s)</c>) is as deep as the loop, and an evaluation that recursed
/// once per level would end the process with a stack overflow.
/// </summary>
/// <remarks>
/// The first <see cref="RecursionLimit"/> levels are evaluated by recursion
/// (<see cref="IVectorOperator.EvaluateDirect"/> of each node), the fastest form for an expression of ordinary depth, with the results as they are
/// (<see cref="VectorResult"/>); the levels below them by
/// <see cref="ExpressionWalk"/>, with explicit stacks and the results stored as <see cref="VectorValue"/>. Both
/// evaluate the inputs of a node before the node, in the order of <see cref="IVectorOperator.GetOperand"/>, and check a node's
/// request (<see cref="IVectorOperator.Enter"/>) before its inputs. A node that is not an
/// <see cref="IVectorOperator"/> (an array, a node implemented outside GpuMath) evaluates itself.
/// </remarks>
internal static class VectorWalk
{
    /// <summary>The number of levels of vector nodes evaluated by recursion before the walk takes over.</summary>
    internal const int RecursionLimit = 16;

    /// <summary>Evaluates <paramref name="root"/> and the expression below it.</summary>
    /// <param name="root">The root node.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">What the root is asked to evaluate.</param>
    /// <returns>The result of the root.</returns>
    public static VectorResult Evaluate(IVectorOperator root, GpuWorkspace workspace,
        scoped in VectorRequest request) =>
        EvaluateDirect(root, workspace, request, RecursionLevel.Walking(RecursionLimit));

    /// <summary>
    /// Evaluates <paramref name="root"/> with <paramref name="levels"/> levels by recursion and the levels below them
    /// walked; tests pass 0 to walk the whole expression.
    /// </summary>
    /// <param name="root">The root node, or a node that evaluates itself.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">What the root is asked to evaluate.</param>
    /// <param name="levels">The number of levels evaluated by recursion.</param>
    /// <returns>The result of the root.</returns>
    internal static VectorResult Evaluate(object root, GpuWorkspace workspace, scoped in VectorRequest request,
        int levels) =>
        EvaluateDirect(root, workspace, request, RecursionLevel.Walking(levels));

    /// <summary>
    /// Evaluates <paramref name="node"/> and the expression below it by recursion, one call of
    /// <see cref="IVectorOperator.EvaluateDirect"/> per level, down to the limit of <paramref name="level"/>; below it
    /// the walk takes over (<c>Evaluate</c>) or the evaluation is refused (<see cref="VectorExpressionRecursion"/>).
    /// </summary>
    /// <param name="node">The node, or a node that evaluates itself.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">What the node is asked to evaluate.</param>
    /// <param name="level">Where the node is in the recursion.</param>
    /// <returns>The result of the node.</returns>
    /// <exception cref="NotSupportedException">
    /// The recursion is bounded and the expression is deeper than its limit.
    /// </exception>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    internal static VectorResult EvaluateDirect(object node, GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        if (node is not IVectorOperator op)
        {
            return EvaluateInput(node, workspace, request);
        }

        if (!level.Recurses)
        {
            if (level.WalksBelow)
            {
                return Walk(op, workspace, request);
            }

            ThrowTooDeep();
        }

        return op.EvaluateDirect(workspace, request, level.Next);
    }

    // Out of line, so that the message is not built in the frame of every level
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooDeep() =>
        throw new NotSupportedException(
            $"The expression is deeper than {VectorExpressionRecursion.MaxCallStackDepth} levels of vector nodes, " +
            "the most EvaluateOnCallStack evaluates by recursion. Use Evaluate, which takes any depth.");

    /// <summary>
    /// Disposes <paramref name="root"/> and every node below it without recursion: each node releases what it holds,
    /// then its inputs are disposed in the order of <see cref="IVectorOperator.GetInput"/>, the first one with
    /// everything below it before the second, as a recursive <c>Dispose</c> did. A node that throws does not stop the
    /// others; one exception is rethrown as it is, several are thrown together in an <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="root">The root node.</param>
    public static void Dispose(IVectorOperator root) =>
        ExpressionDisposal.Dispose<Disposer, object>(root, default);

    // The levels below the recursive ones. Kept out of the recursive method: the stacks of the walk keep their first
    // items in its frame, which every recursive level would otherwise clear.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static VectorResult Walk(IVectorOperator root, GpuWorkspace workspace, scoped in VectorRequest request)
    {
        var walker = new Walker(workspace);
        return ExpressionWalk.Evaluate<Walker, object, VectorValue, VectorRequest>(ref walker, root, request).Result;
    }

    // A node that evaluates itself: an array, or a node implemented outside GpuMath. Each kind has its own method, so
    // that the frame of this one stays small.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static VectorResult EvaluateInput(object node, GpuWorkspace workspace, scoped in VectorRequest request) =>
        node switch
        {
            IGpuBigIntVectorExpression vector => EvaluateInput(vector, workspace, request),
            IGpuBigFloatVectorExpression vector => EvaluateInput(vector, workspace, request),
            IGpuBoolVectorExpression flags => EvaluateInput(flags, workspace),
            _ => throw new ArgumentException($"{node.GetType()} is not a vector expression.", nameof(node))
        };

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static VectorResult EvaluateInput(IGpuBigIntVectorExpression vector, GpuWorkspace workspace,
        scoped in VectorRequest request) =>
        VectorResult.Of(DeviceCounted.Evaluate(vector, workspace, request.Offset, request.Count, request.Target.Words));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static VectorResult EvaluateInput(IGpuBigFloatVectorExpression vector, GpuWorkspace workspace,
        scoped in VectorRequest request) =>
        VectorResult.Of(DeviceCounted.Evaluate(vector, workspace, request.Offset, request.Count,
            request.Target.FloatArray));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static VectorResult EvaluateInput(IGpuBoolVectorExpression flags, GpuWorkspace workspace) =>
        VectorResult.Of(DeviceCounted.Evaluate(flags, workspace));

    // The vector tree for ExpressionWalk. The results are views of the nodes' own buffers or of the arrays; none is
    // owned, so a failed walk has nothing to release.
    private readonly struct Walker(GpuWorkspace workspace) : IExpressionWalker<object, VectorValue, VectorRequest>
    {
        /// <inheritdoc/>
        public int OperandCount(object node, VectorRequest request) =>
            node is IVectorOperator op && op.EvaluatesOperands(request) ? op.OperandCount : 0;

        /// <inheritdoc/>
        public object GetOperand(object node, int index) => ((IVectorOperator)node).GetOperand(index);

        /// <inheritdoc/>
        public VectorRequest OperandTarget(object node, VectorRequest request) =>
            ((IVectorOperator)node).OperandRequest(request);

        /// <inheritdoc/>
        public void Enter(object node, VectorRequest request)
        {
            if (node is IVectorOperator op)
            {
                op.Enter(request);
            }
        }

        // A vector node that evaluates no input for this request, or a node that evaluates itself
        /// <inheritdoc/>
        public VectorValue EvaluateLeaf(object node, VectorRequest request) =>
            VectorValue.Of(node is IVectorOperator op
                ? op.Apply(workspace, request, default, default)
                : EvaluateInput(node, workspace, request));

        /// <inheritdoc/>
        public VectorValue Apply(object node, VectorValue first, VectorValue second, VectorRequest request) =>
            VectorValue.Of(((IVectorOperator)node).Apply(workspace, request, first.Result, second.Result));

        /// <inheritdoc/>
        public void Release(VectorValue value)
        {
        }
    }

    // The vector tree for ExpressionDisposal: a vector node releases its own buffers and has its inputs; any other
    // node is disposed whole
    private readonly struct Disposer : IDisposalTree<object>
    {
        /// <inheritdoc/>
        public void Release(object node)
        {
            if (node is IVectorOperator op)
            {
                op.DisposeOwn();
            }
            else
            {
                ((IDisposable)node).Dispose();
            }
        }

        /// <inheritdoc/>
        public int InputCount(object node) => node is IVectorOperator op ? op.OperandCount : 0;

        /// <inheritdoc/>
        public object GetInput(object node, int index) => ((IVectorOperator)node).GetInput(index);
    }
}
