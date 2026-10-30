namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The immediate post-dominators of a graph given by its successor lists, from a virtual exit to which the exit node and
/// every node that cannot reach it lead (Cooper, Harvey and Kennedy, on the reverse graph), and the influence regions and
/// meeting points of its branches that <see cref="UniformityAnalysis"/> needs.
/// </summary>
internal sealed class PostDominatorTree
{
    private readonly ImmutableArray<int>[] _successors;
    private readonly int[] _postDominator;
    private readonly int[] _predecessorCount;
    private readonly ImmutableArray<int>?[] _regions;
    private readonly ImmutableArray<int>?[] _joins;

    /// <param name="successors">The successors of each node.</param>
    /// <param name="exit">The node where the graph ends.</param>
    public PostDominatorTree(ImmutableArray<int>[] successors, int exit)
    {
        _successors = successors;
        _postDominator = Solve(AddVirtualExit(successors, exit));
        _predecessorCount = new int[successors.Length];
        foreach (var nodeSuccessors in successors)
        {
            foreach (var successor in nodeSuccessors)
            {
                _predecessorCount[successor]++;
            }
        }

        _regions = new ImmutableArray<int>?[successors.Length];
        _joins = new ImmutableArray<int>?[successors.Length];
    }

    /// <summary>
    /// The immediate post-dominator of <paramref name="node"/>; the number of nodes when it is the virtual exit.
    /// </summary>
    public int GetImmediatePostDominator(int node) => _postDominator[node];

    /// <summary>The influence region of a branch: the nodes its successors reach before its immediate post-dominator.</summary>
    public ImmutableArray<int> GetRegion(int branch)
    {
        if (_regions[branch] is { } cached)
        {
            return cached;
        }

        var stop = _postDominator[branch];
        var reached = new bool[_successors.Length];
        var region = ImmutableArray.CreateBuilder<int>();
        var stack = ImmutableArray.CreateBuilder<int>();
        foreach (var successor in _successors[branch])
        {
            if (successor != stop && !reached[successor])
            {
                reached[successor] = true;
                stack.Add(successor);
            }
        }

        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            region.Add(node);
            foreach (var successor in _successors[node])
            {
                if (successor != stop && !reached[successor])
                {
                    reached[successor] = true;
                    stack.Add(successor);
                }
            }
        }

        var result = region.ToImmutable();
        _regions[branch] = result;
        return result;
    }

    /// <summary>
    /// The meeting points of a branch: the nodes with several predecessors, up to and including its immediate
    /// post-dominator, that two of its successors reach without coming back through the branch.
    /// </summary>
    public ImmutableArray<int> GetJoins(int branch)
    {
        if (_joins[branch] is { } cached)
        {
            return cached;
        }

        var stop = _postDominator[branch];
        var meets = FindMeetings(branch, stop);
        var joins = ImmutableArray.CreateBuilder<int>();
        for (var node = 0; node < meets.Length; node++)
        {
            if (meets[node] && _predecessorCount[node] >= 2)
            {
                joins.Add(node);
            }
        }

        // [WHY] A branch that its own region comes back to is the exit test of a loop: the threads leave the loop in
        // different iterations, so what the loop writes differs after it (LLVM's cycle exit divergence,
        // propagateCycleExitDivergence). The walk of FindMeetings stops at the branch and never sees the exit meet the
        // body.
        if (stop < _successors.Length && GetRegion(branch).Contains(branch) && !joins.Contains(stop))
        {
            joins.Add(stop);
        }

        var result = joins.ToImmutable();
        _joins[branch] = result;
        return result;
    }

    // The nodes that two successors of branch reach, walking each path up to stop and not through branch again
    private bool[] FindMeetings(int branch, int stop)
    {
        var reachedBy = new int[_successors.Length];
        var meets = new bool[_successors.Length];
        for (var i = 0; i < reachedBy.Length; i++)
        {
            reachedBy[i] = -1;
        }

        var successors = _successors[branch];
        var reached = new bool[_successors.Length];
        var stack = ImmutableArray.CreateBuilder<int>();
        for (var path = 0; path < successors.Length; path++)
        {
            Array.Clear(reached, 0, reached.Length);
            stack.Add(successors[path]);
            while (stack.Count > 0)
            {
                var node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (node == branch || reached[node])
                {
                    continue;
                }

                reached[node] = true;
                if (reachedBy[node] < 0)
                {
                    reachedBy[node] = path;
                }
                else if (reachedBy[node] != path)
                {
                    meets[node] = true;
                }

                if (node != stop)
                {
                    stack.AddRange(_successors[node]);
                }
            }
        }

        return meets;
    }

    // The successor lists with a virtual exit (the last index), which the exit node and every node that cannot reach it
    // lead to
    private static ImmutableArray<int>.Builder[] AddVirtualExit(ImmutableArray<int>[] successors, int exit)
    {
        var count = successors.Length;
        var virtualExit = count;
        var withExit = new ImmutableArray<int>.Builder[count + 1];
        for (var i = 0; i < count; i++)
        {
            withExit[i] = ImmutableArray.CreateBuilder<int>();
            withExit[i].AddRange(successors[i]);
        }

        withExit[virtualExit] = ImmutableArray.CreateBuilder<int>();
        withExit[exit].Add(virtualExit);

        var reaches = new bool[count + 1];
        MarkReverseReachable(virtualExit, Predecessors(withExit), reaches);
        for (var i = 0; i < count; i++)
        {
            if (!reaches[i])
            {
                withExit[i].Add(virtualExit);
            }
        }

        return withExit;
    }

    // The iterative algorithm on the reverse graph, in reverse post-order from the virtual exit
    private static int[] Solve(ImmutableArray<int>.Builder[] successors)
    {
        var virtualExit = successors.Length - 1;
        var order = ReversePostOrder(virtualExit, Predecessors(successors), out var position);
        var postDominator = new int[successors.Length];
        for (var i = 0; i < postDominator.Length; i++)
        {
            postDominator[i] = -1;
        }

        postDominator[virtualExit] = virtualExit;
        bool changed;
        do
        {
            changed = false;
            foreach (var node in order)
            {
                if (node == virtualExit)
                {
                    continue;
                }

                var candidate = -1;
                foreach (var successor in successors[node])
                {
                    if (postDominator[successor] >= 0)
                    {
                        candidate = candidate < 0 ? successor : Intersect(successor, candidate, postDominator, position);
                    }
                }

                if (candidate >= 0 && postDominator[node] != candidate)
                {
                    postDominator[node] = candidate;
                    changed = true;
                }
            }
        } while (changed);

        return postDominator;
    }

    private static ImmutableArray<int>.Builder[] Predecessors(ImmutableArray<int>.Builder[] successors)
    {
        var predecessors = new ImmutableArray<int>.Builder[successors.Length];
        for (var i = 0; i < predecessors.Length; i++)
        {
            predecessors[i] = ImmutableArray.CreateBuilder<int>();
        }

        for (var i = 0; i < successors.Length; i++)
        {
            foreach (var successor in successors[i])
            {
                predecessors[successor].Add(i);
            }
        }

        return predecessors;
    }

    private static void MarkReverseReachable(int start, ImmutableArray<int>.Builder[] predecessors, bool[] reached)
    {
        var stack = ImmutableArray.CreateBuilder<int>();
        stack.Add(start);
        reached[start] = true;
        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            foreach (var predecessor in predecessors[node])
            {
                if (!reached[predecessor])
                {
                    reached[predecessor] = true;
                    stack.Add(predecessor);
                }
            }
        }
    }

    // The nodes in reverse post-order of a depth-first walk of the reverse graph, and the post-order number of each
    private static ImmutableArray<int> ReversePostOrder(int start, ImmutableArray<int>.Builder[] predecessors,
        out int[] position)
    {
        position = new int[predecessors.Length];
        var visited = new bool[predecessors.Length];
        var postOrder = ImmutableArray.CreateBuilder<int>(predecessors.Length);
        var stack = ImmutableArray.CreateBuilder<(int Node, int Next)>();
        stack.Add((start, 0));
        visited[start] = true;
        while (stack.Count > 0)
        {
            var (node, next) = stack[stack.Count - 1];
            if (next < predecessors[node].Count)
            {
                stack[stack.Count - 1] = (node, next + 1);
                var predecessor = predecessors[node][next];
                if (!visited[predecessor])
                {
                    visited[predecessor] = true;
                    stack.Add((predecessor, 0));
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            position[node] = postOrder.Count;
            postOrder.Add(node);
        }

        postOrder.Reverse();
        return postOrder.ToImmutable();
    }

    private static int Intersect(int a, int b, int[] postDominator, int[] position)
    {
        while (a != b)
        {
            while (position[a] < position[b])
            {
                a = postDominator[a];
            }

            while (position[b] < position[a])
            {
                b = postDominator[b];
            }
        }

        return a;
    }
}
