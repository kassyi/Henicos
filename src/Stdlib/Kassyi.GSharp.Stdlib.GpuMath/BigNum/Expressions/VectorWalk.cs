using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Evaluates and disposes vector expressions (<see cref="IVectorOperator"/>) without one call stack frame per level:
/// an expression built in a loop (<c>v = v.SelectAdd(s)</c>) is as deep as the loop, and an evaluation that recursed
/// once per level would end the process with a stack overflow.
/// </summary>
/// <remarks>
/// The first <see cref="DefaultRecursionLimit"/> levels are evaluated by recursion, the fastest form for an expression
/// of ordinary depth; the levels below them by <see cref="ExpressionWalk"/>, with explicit stacks. Both evaluate the
/// inputs of a node before the node, in the order of <see cref="IVectorOperator.GetOperand"/>, and check a node's
/// request (<see cref="IVectorOperator.Enter"/>) before its inputs. A node that is not an
/// <see cref="IVectorOperator"/> (an array, a node implemented outside GpuMath) evaluates itself.
/// </remarks>
internal static class VectorWalk
{
    /// <summary>The number of levels of vector nodes evaluated by recursion before the walk takes over.</summary>
    internal const int DefaultRecursionLimit = 16;

    // The number of levels evaluated by recursion on this thread; tests set 0 to walk every level
    [ThreadStatic]
    private static int? t_recursionLimit;

    /// <summary>
    /// Gets or sets the number of levels of vector nodes evaluated by recursion on this thread; the levels below are
    /// walked with explicit stacks. Tests set 0 to walk the whole expression; <see langword="null"/> restores
    /// <see cref="DefaultRecursionLimit"/>.
    /// </summary>
    internal static int? RecursionLimit
    {
        get => t_recursionLimit;
        set => t_recursionLimit = value;
    }

    /// <summary>Evaluates <paramref name="root"/> and the expression below it.</summary>
    /// <param name="root">The root node.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="request">What the root is asked to evaluate.</param>
    /// <returns>The result of the root.</returns>
    public static VectorValue Evaluate(IVectorOperator root, GpuWorkspace workspace, in VectorRequest request) =>
        Evaluate(root, workspace, request, t_recursionLimit ?? DefaultRecursionLimit);

    /// <summary>
    /// Disposes the inputs of <paramref name="node"/> (which released what it holds itself) and every node below them:
    /// each node releases what it holds, then its inputs are disposed in the order of
    /// <see cref="IVectorOperator.GetInput"/>, the first one with everything below it before the second, as a
    /// recursive <c>Dispose</c> did. A node that throws does not stop the others; one exception is rethrown as it is,
    /// several together in an <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="node">The node whose inputs are disposed.</param>
    public static void DisposeInputs(IVectorOperator node)
    {
        var pending = default(WalkStack<object>);
        List<Exception>? errors = null;
        try
        {
            PushInputs(node, ref pending);
            while (pending.Count > 0)
            {
                DisposeOrExpand(pending.Pop(), ref pending, ref errors);
            }
        }
        finally
        {
            pending.Dispose();
        }

        if (errors is null)
        {
            return;
        }

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        throw new AggregateException(errors);
    }

    // A vector node releases what it holds and pushes its inputs, the last one first so that the first one is disposed
    // first; any other node is disposed. An exception is collected instead of stopping the walk.
    private static void DisposeOrExpand(object node, ref WalkStack<object> pending, ref List<Exception>? errors)
    {
        try
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
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (node is IVectorOperator expanded)
        {
            PushInputs(expanded, ref pending);
        }
    }

    // The last input first, so that the first one is disposed first
    private static void PushInputs(IVectorOperator node, ref WalkStack<object> pending)
    {
        for (var i = node.OperandCount - 1; i >= 0; i--)
        {
            pending.Push(node.GetInput(i));
        }
    }

    // The node by recursion within the first levels, otherwise by ExpressionWalk with the levels below it
    private static VectorValue Evaluate(object node, GpuWorkspace workspace, in VectorRequest request, int levels)
    {
        if (node is not IVectorOperator op)
        {
            return EvaluateInput(node, workspace, request);
        }

        if (levels == 0)
        {
            var walker = new Walker(workspace);
            return ExpressionWalk.Evaluate<Walker, object, VectorValue, VectorRequest>(ref walker, op, request);
        }

        op.Enter(request);
        if (!op.EvaluatesOperands(request))
        {
            return op.Apply(workspace, request, default, default);
        }

        var operandRequest = op.OperandRequest(request);
        var first = Evaluate(op.GetOperand(0), workspace, operandRequest, levels - 1);
        var second = op.OperandCount == 2
            ? Evaluate(op.GetOperand(1), workspace, operandRequest, levels - 1)
            : default;
        return op.Apply(workspace, request, first, second);
    }

    // A node that evaluates itself: an array, or a node implemented outside GpuMath
    private static VectorValue EvaluateInput(object node, GpuWorkspace workspace, in VectorRequest request) =>
        node switch
        {
            IGpuBigIntVectorExpression vector => VectorValue.Of(DeviceCounted.Evaluate(vector, workspace,
                request.Offset, request.Count, request.Target.Words)),
            IGpuBigFloatVectorExpression vector => VectorValue.Of(DeviceCounted.Evaluate(vector, workspace,
                request.Offset, request.Count, request.Target.FloatArray)),
            IGpuBoolVectorExpression flags => VectorValue.Of(DeviceCounted.Evaluate(flags, workspace)),
            _ => throw new ArgumentException($"{node.GetType()} is not a vector expression.", nameof(node))
        };

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
            node is IVectorOperator op
                ? op.Apply(workspace, request, default, default)
                : EvaluateInput(node, workspace, request);

        /// <inheritdoc/>
        public VectorValue Apply(object node, VectorValue first, VectorValue second, VectorRequest request) =>
            ((IVectorOperator)node).Apply(workspace, request, first, second);

        /// <inheritdoc/>
        public void Release(VectorValue value)
        {
        }
    }
}
