using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The influence regions of the thread-variant branches of a <see cref="UniformityAnalysis"/>: the variance of each
/// branch is added to the nodes of its region and to the values written there where its paths meet again (LLVM's sync
/// dependence); both only grow, so <see cref="UniformitySolver"/> alternates them with the data flow until
/// neither does.
/// </summary>
internal sealed class UniformityRegions(UniformityState state)
{
    // The values written in the influence region of each branch, each once, computed on first use
    private ImmutableArray<int>?[] _writtenInRegion = [];

    /// <summary>Forgets the cached regions for a graph of <paramref name="count"/> nodes.</summary>
    public void Reset(int count) => _writtenInRegion = new ImmutableArray<int>?[count];

    // The variance of each branch at the end of its node; adds the variance of a branch to the nodes of its region, and
    // to the values written in the region where its paths meet again
    public bool Update(UniformitySolver solver, ThreadVariance[] current)
    {
        var changed = false;
        for (var node = 0; node < state.NodeCount; node++)
        {
            if (state.Graph.Nodes[node] is not { Condition: { } condition, Graph: { } graph } ||
                state.Successors[node].Length < 2)
            {
                continue;
            }

            solver.RunNode(node, current);
            var variance = state.Branch[node] | state.Reader.Evaluate(condition, graph, current);
            if (variance == state.Branch[node])
            {
                continue;
            }

            state.Branch[node] = variance;
            var region = state.PostDominators.GetRegion(node);
            changed |= AddToRegion(region, variance);
            changed |= AddAtJoins(node, variance);
        }

        return changed;
    }

    private bool AddToRegion(ImmutableArray<int> region, ThreadVariance variance)
    {
        var changed = false;
        foreach (var member in region)
        {
            var merged = state.Region[member] | variance;
            if (merged != state.Region[member])
            {
                state.Region[member] = merged;
                changed = true;
            }
        }

        return changed;
    }

    // A value written in the region may differ where the paths of the branch meet again
    private bool AddAtJoins(int branch, ThreadVariance variance)
    {
        var written = GetWrittenInRegion(branch);
        var changed = false;
        foreach (var join in state.PostDominators.GetJoins(branch))
        {
            var target = state.In[join];
            foreach (var value in written)
            {
                var merged = target[value] | variance;
                changed |= merged != target[value];
                target[value] = merged;
            }
        }

        return changed;
    }

    // The values that the nodes of the influence region of branch write, each once; the region does not change,
    // only the variance of the branch grows
    private ImmutableArray<int> GetWrittenInRegion(int branch)
    {
        if (_writtenInRegion[branch] is { } cached)
        {
            return cached;
        }

        var seen = new bool[state.Recorder.ValueCount];
        var written = ImmutableArray.CreateBuilder<int>();
        foreach (var member in state.PostDominators.GetRegion(branch))
        {
            foreach (var value in state.NodeAt(member).Written)
            {
                if (!seen[value])
                {
                    seen[value] = true;
                    written.Add(value);
                }
            }
        }

        var result = written.ToImmutable();
        _writtenInRegion[branch] = result;
        return result;
    }
}
