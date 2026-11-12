using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Disposes an expression tree with an explicit stack instead of the call stack: an expression built in a loop is as
/// deep as the loop, and a <c>Dispose</c> that recursed once per level would end the process with a stack overflow.
/// </summary>
internal static class ExpressionDisposal
{
    /// <summary>
    /// Disposes <paramref name="root"/> and every node below it: each node releases what it holds, then its inputs are
    /// disposed, the first one with everything below it before the second (the order of a recursive <c>Dispose</c>). A
    /// node that throws does not stop the others; one exception is rethrown as it is, several are thrown together in an
    /// <see cref="AggregateException"/>.
    /// </summary>
    /// <typeparam name="TTree">The tree (<see cref="IDisposalTree{TNode}"/>).</typeparam>
    /// <typeparam name="TNode">The node type.</typeparam>
    /// <param name="root">The root node.</param>
    /// <param name="tree">The tree.</param>
    public static void Dispose<TTree, TNode>(TNode root, TTree tree)
        where TTree : struct, IDisposalTree<TNode>
        where TNode : class
    {
        var pending = default(WalkStack<TNode>);
        List<Exception>? errors = null;
        try
        {
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                try
                {
                    tree.Release(node);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }

                // The last input first, so that the first one is disposed first
                for (var i = tree.InputCount(node) - 1; i >= 0; i--)
                {
                    pending.Push(tree.GetInput(node, i));
                }
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
}
