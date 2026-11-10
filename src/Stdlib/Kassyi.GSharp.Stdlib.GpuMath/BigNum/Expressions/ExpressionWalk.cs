namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// What the post-order walk of ExpressionWalk asks of the expression tree it evaluates: the signed tree (SignedTree) and
// the unsigned one (BigIntExpressionVisitor)
internal interface IExpressionWalker<TNode, TValue, TTarget>
    where TNode : class
    where TValue : struct
{
    // The operands of an operator node, 1 or 2; 0 for a node that evaluates itself (a leaf, or a node implemented
    // outside GpuMath)
    int OperandCount(TNode node);

    TNode GetOperand(TNode node, int index);

    // The target the operands of the node are evaluated into
    TTarget OperandTarget(TNode node, TTarget target);

    // Called for each node before any of its operands is evaluated; a node rejects a target it cannot use here
    void Enter(TNode node, TTarget target);

    // A node without operands, which evaluates itself
    TValue EvaluateLeaf(TNode node, TTarget target);

    // An operator node applied to its evaluated operands. The call owns them and releases them, also when it fails;
    // second is the default value for a unary node.
    TValue Apply(TNode node, TValue first, TValue second, TTarget target);

    void Release(TValue value);
}

// Evaluates an expression tree in post order with stacks on the heap instead of the call stack. An expression built in
// a loop (e = x + e, or a left fold) is as deep as the loop along either operand; a walk that recursed once per level
// would end the process with a stack overflow. The operands of a node are evaluated before the node, the left one
// first, as a recursive evaluation does.
internal static class ExpressionWalk
{
    public static TValue Evaluate<TWalker, TNode, TValue, TTarget>(ref TWalker walker, TNode root, TTarget target)
        where TWalker : IExpressionWalker<TNode, TValue, TTarget>, allows ref struct
        where TNode : class
        where TValue : struct
    {
        var frames = new PooledStack<Frame<TNode, TTarget>>();
        var values = new PooledStack<TValue>();
        try
        {
            walker.Enter(root, target);
            frames.Push(new Frame<TNode, TTarget>(root, target, walker.OperandCount(root)));
            while (frames.Count > 0)
            {
                ref var frame = ref frames.Peek();
                if (frame.Next < frame.OperandCount)
                {
                    var operand = walker.GetOperand(frame.Node, frame.Next);
                    var operandTarget = walker.OperandTarget(frame.Node, frame.Target);
                    frame.Next++;
                    walker.Enter(operand, operandTarget);
                    frames.Push(new Frame<TNode, TTarget>(operand, operandTarget, walker.OperandCount(operand)));
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
    private static void ReleaseAll<TWalker, TNode, TValue, TTarget>(ref TWalker walker, ref PooledStack<TValue> values,
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
