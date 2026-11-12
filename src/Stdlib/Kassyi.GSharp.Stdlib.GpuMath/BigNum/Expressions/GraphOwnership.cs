namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Records which expression nodes a live CUDA graph owns.
/// </summary>
/// <remarks>
/// A graph replays the buffers its expression's nodes hold, so the expression must not be evaluated, compiled again or
/// changed while the graph is alive. The check sits in <see cref="GpuBigIntExpr"/>, through which every public
/// evaluation of a signed expression goes.
/// </remarks>
internal static class GraphOwnership
{
    private static readonly ConditionalWeakTable<IGpuBigIntExpression, object> s_owned = new();
    private static readonly object s_owner = new();
    private static readonly object s_lock = new();

    public static void Take(IGpuBigIntExpression node)
    {
        var nodes = new HashSet<IGpuBigIntExpression>(ReferenceEqualityComparer.Instance);
        CollectSubtree(node, nodes);

        lock (s_lock)
        {
            foreach (var n in nodes)
            {
                if (s_owned.TryGetValue(n, out _))
                {
                    throw new InvalidOperationException(
                        "The expression was compiled into a CUDA graph; dispose the graph instead.");
                }
            }

            foreach (var n in nodes)
            {
                s_owned.AddOrUpdate(n, s_owner);
            }
        }
    }

    public static void Release(IGpuBigIntExpression node)
    {
        var nodes = new HashSet<IGpuBigIntExpression>(ReferenceEqualityComparer.Instance);
        CollectSubtree(node, nodes);

        lock (s_lock)
        {
            foreach (var n in nodes)
            {
                s_owned.Remove(n);
            }
        }
    }

    public static void ThrowIfOwned(IGpuBigIntExpression node)
    {
        var nodes = new HashSet<IGpuBigIntExpression>(ReferenceEqualityComparer.Instance);
        CollectSubtree(node, nodes);

        foreach (var n in nodes)
        {
            if (s_owned.TryGetValue(n, out _))
            {
                throw new InvalidOperationException(
                    "The expression was compiled into a CUDA graph; dispose the graph instead.");
            }
        }
    }

    // Without recursion: an expression built in a loop is as deep as the loop
    private static void CollectSubtree(IGpuBigIntExpression node, HashSet<IGpuBigIntExpression> nodes) =>
        SignedTree.Collect(node, nodes);
}
