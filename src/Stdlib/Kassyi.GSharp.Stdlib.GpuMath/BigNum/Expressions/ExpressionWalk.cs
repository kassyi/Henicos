namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Evaluates an expression tree in post order with explicit stacks instead of the call stack (<see cref="WalkStack{T}"/>:
/// in the frame of the walk for an expression of ordinary depth, rented past that). An expression built in a loop
/// (<c>e = x + e</c>, or a left fold) is as deep as the loop along either operand; a walk that recursed once per level
/// would end the process with a stack overflow.
/// </summary>
internal static class ExpressionWalk
{
    /// <summary>
    /// Evaluates the tree below <paramref name="root"/>: the operands of a node before the node, the left one first,
    /// as a recursive evaluation does. When a node fails, the values evaluated before it are released, the latest first.
    /// </summary>
    /// <typeparam name="TWalker">
    /// The type that walks the tree (<see cref="IExpressionWalker{TNode, TValue, TTarget}"/>).
    /// </typeparam>
    /// <typeparam name="TNode">The node type.</typeparam>
    /// <typeparam name="TValue">The value of an evaluated node.</typeparam>
    /// <typeparam name="TTarget">What a node is evaluated into.</typeparam>
    /// <param name="walker">The walker of the tree; its state changes as the walk evaluates nodes.</param>
    /// <param name="root">The root node.</param>
    /// <param name="target">The target of the root.</param>
    /// <returns>The value of the root.</returns>
    public static TValue Evaluate<TWalker, TNode, TValue, TTarget>(ref TWalker walker, TNode root, TTarget target)
        where TWalker : IExpressionWalker<TNode, TValue, TTarget>, allows ref struct
        where TNode : class
        where TValue : struct
    {
        var frames = default(WalkStack<Frame<TNode, TTarget>>);
        var values = default(WalkStack<TValue>);
        try
        {
            walker.Enter(root, target);
            frames.Push(new Frame<TNode, TTarget>(root, target, walker.OperandCount(root, target)));
            while (frames.Count > 0)
            {
                ref var frame = ref frames.Peek();
                if (frame.Next < frame.OperandCount)
                {
                    var operand = walker.GetOperand(frame.Node, frame.Next);
                    var operandTarget = walker.OperandTarget(frame.Node, frame.Target);
                    frame.Next++;
                    walker.Enter(operand, operandTarget);
                    frames.Push(new Frame<TNode, TTarget>(operand, operandTarget,
                        walker.OperandCount(operand, operandTarget)));
                    continue;
                }

                var done = frames.Pop();
                if (done.OperandCount == 0)
                {
                    values.Push(walker.EvaluateLeaf(done.Node, done.Target));
                    continue;
                }

                // The operands go to Apply, which releases them
                var second = done.OperandCount == 2 ? values.Pop() : default;
                var first = values.Pop();
                values.Push(walker.Apply(done.Node, first, second, done.Target));
            }

            return values.Pop();
        }
        catch (Exception failure)
        {
            ReleaseAll<TWalker, TNode, TValue, TTarget>(ref walker, ref values, failure);
            throw;
        }
        finally
        {
            frames.Dispose();
            values.Dispose();
        }
    }

    // Releases the values evaluated before a failure, the latest first (the order in which a recursive evaluation
    // unwound). A release that throws does not stop the others; its exception is thrown together with the failure.
    private static void ReleaseAll<TWalker, TNode, TValue, TTarget>(ref TWalker walker, ref WalkStack<TValue> values,
        Exception failure)
        where TWalker : IExpressionWalker<TNode, TValue, TTarget>, allows ref struct
        where TNode : class
        where TValue : struct
    {
        List<Exception>? errors = null;
        while (values.Count > 0)
        {
            try
            {
                walker.Release(values.Pop());
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

    // A node to evaluate, the target it writes into, its operand count, and the index of the next operand to evaluate
    private struct Frame<TNode, TTarget>(TNode node, TTarget target, int operandCount)
    {
        public readonly TNode Node = node;
        public readonly TTarget Target = target;
        public readonly int OperandCount = operandCount;
        public int Next;
    }
}
