using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides, for a <see cref="UniformityAnalysis"/>, whether a barrier or barrier-reaching call is divergent: in the
/// influence region of a divergent branch of the method, or because of the callee and the variance of the arguments
/// (§3.2.4), and what the barriers of the method give its callers.
/// </summary>
/// <remarks>
/// A barrier in the influence region of a divergent branch is <see cref="BarrierDivergence.Condition"/> when the threads
/// that skip it (a successor of the branch that does not reach the barrier without coming back through the branch) can
/// reach any point where the state machine splits, the barrier itself included; otherwise only on their way out of the
/// method, <see cref="BarrierDivergence.ExitOnly"/>. A block barrier is divergent only on a branch that varies within a
/// block; a grid barrier also on one that varies between blocks.
/// </remarks>
internal sealed class BarrierClassifier(UniformityState state)
{
    /// <summary>
    /// Whether only some of the threads that must reach the barrier or barrier-reaching call <paramref name="barrier"/>
    /// reach it because a divergent branch of this method decides it.
    /// </summary>
    public BarrierReport GetBarrierReport(SyntaxNode barrier, SyncScope scope)
    {
        if (!state.Recorder.SplitsAt.TryGetValue(barrier, out var nodes))
        {
            return default;
        }

        var result = default(BarrierReport);
        foreach (var node in nodes)
        {
            for (var branch = 0; branch < state.Branch.Length; branch++)
            {
                if (Diverges(state.Branch[branch], scope) && state.PostDominators.GetRegion(branch).Contains(node))
                {
                    result = Combine(result,
                        new BarrierReport(Classify(branch, node), state.Prober.IsCertain(branch, scope)));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the call <paramref name="call"/> to a device function that reaches a block barrier makes only some
    /// threads reach a barrier inside the callee, given which arguments vary (§3.2.4).
    /// </summary>
    public BarrierReport GetCallSiteReport(SyntaxNode call)
    {
        if (!state.Recorder.Sites.TryGetValue(call, out var site) || site.Call is not { } invocation)
        {
            return default;
        }

        var masks = GetSiteMasks(call);
        var callee = invocation.TargetMethod;
        return state.Reader.TryGetDeviceFunction(callee, out var definition, out var declaration)
            ? GetLocalCallSiteReport(call, definition, declaration, masks)
            : GetImportedCallSiteReport(call, callee, masks);
    }

    /// <summary>
    /// The conditions of the divergent branches that make <paramref name="barrier"/> exit only (§3.2.3). Empty unless the
    /// barrier is <see cref="BarrierDivergence.ExitOnly"/> as a whole.
    /// </summary>
    public ImmutableArray<SyntaxNode> GetExitBranches(SyntaxNode barrier, SyncScope scope)
    {
        if (GetBarrierReport(barrier, scope).Divergence != BarrierDivergence.ExitOnly ||
            !state.Recorder.SplitsAt.TryGetValue(barrier, out var nodes))
        {
            return ImmutableArray<SyntaxNode>.Empty;
        }

        var branches = ImmutableArray.CreateBuilder<SyntaxNode>();
        foreach (var node in nodes)
        {
            for (var branch = 0; branch < state.Branch.Length; branch++)
            {
                if (Diverges(state.Branch[branch], scope) &&
                    state.PostDominators.GetRegion(branch).Contains(node) &&
                    Classify(branch, node) == BarrierDivergence.ExitOnly &&
                    state.Graph.Nodes[branch].Condition is { Syntax: { } condition } &&
                    !branches.Contains(condition))
                {
                    branches.Add(condition);
                }
            }
        }

        return branches.ToImmutable();
    }

    /// <summary>
    /// Whether the barrier-reaching call <paramref name="call"/>, outside any divergent branch of this method, lets some
    /// threads leave the callee early before one of its barriers while nothing after the call splits the state machine.
    /// </summary>
    public bool IsExitOnlyCall(SyntaxNode call) =>
        GetBarrierReport(call, SyncScope.Block).Divergence == BarrierDivergence.None &&
        GetCallComposition(call).Divergence == BarrierDivergence.ExitOnly;

    /// <summary>
    /// The divergence of the barriers of <paramref name="method"/> that its export record carries for callers in other
    /// assemblies (B7): for each parameter alone, whether a thread-variant argument makes a barrier diverge.
    /// </summary>
    public static ExportedBarrierDivergence GetExportedDivergence(IMethodSymbol method, Compilation compilation)
    {
        var definition = method.OriginalDefinition;
        if (!SuspensionAnalysis.TryGetDeviceFunctionBody(definition, compilation, out var declaration) ||
            !SuspensionAnalysis.ReachesBlockBarrier(definition, compilation))
        {
            return default;
        }

        var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        if (!MethodFlowGraph.TryCreate(model, declaration, out var graph))
        {
            return default;
        }

        var analysis = UniformityAnalysis.AnalyzeBody(graph, model, definition, default,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
        var uniform = analysis.Summarize().Barrier;
        var condition = 0;
        var exitOnly = 0;
        foreach (var parameter in definition.Parameters)
        {
            var summary = analysis.GetCalleeSummary(definition, declaration,
                new ArgumentMasks(ArgumentMasks.Bit(parameter.Ordinal), 0, 0));
            var bit = 1 << Math.Min(parameter.Ordinal, 31);
            if (summary.Barrier == BarrierDivergence.Condition && uniform != BarrierDivergence.Condition)
            {
                condition |= bit;
            }
            else if (summary.Barrier == BarrierDivergence.ExitOnly && uniform == BarrierDivergence.None)
            {
                exitOnly |= bit;
            }
        }

        return new ExportedBarrierDivergence(condition, exitOnly, uniform == BarrierDivergence.ExitOnly);
    }

    /// <summary>
    /// Σ of the analyzed method (§3.2.4): its barriers in the region of a divergent branch, and for each
    /// barrier-reaching call outside such a region, what the callee gives for its arguments.
    /// </summary>
    public BarrierReport SummarizeBarriers()
    {
        var result = default(BarrierReport);
        foreach (var pair in state.Recorder.Sites)
        {
            var report = GetBarrierReport(pair.Key, pair.Value.Scope);
            if (report.Divergence == BarrierDivergence.None && pair.Value.Call != null)
            {
                report = GetCallComposition(pair.Key);
            }

            result = Combine(result, report);
        }

        return result;
    }

    /// <summary>Records the argument masks of a barrier-reaching call in the solved state, while they are recorded.</summary>
    public void RecordSiteMasks(IInvocationOperation call, ControlFlowGraph graph, ThreadVariance[] current)
    {
        if (state.SiteMasks is not { } siteMasks)
        {
            return;
        }

        var masks = state.Reader.GetArgumentMasks(call, graph, current);
        siteMasks[call.Syntax] = siteMasks.TryGetValue(call.Syntax, out var seen)
            ? new ArgumentMasks(seen.Block | masks.Block, seen.Grid | masks.Grid, seen.Identity & masks.Identity)
            : masks;
    }

    private static bool Diverges(ThreadVariance branch, SyncScope scope) =>
        scope == SyncScope.Grid ? branch != ThreadVariance.Uniform : branch == ThreadVariance.Block;

    // A barrier at node in the influence region of branch: Condition when a successor of the branch that does not reach
    // the barrier (without coming back through the branch) can reach a split point, the barrier itself included
    private BarrierDivergence Classify(int branch, int node)
    {
        var skips = false;
        foreach (var successor in state.Successors[branch])
        {
            if (Reaches(successor, node, avoid: branch))
            {
                continue;
            }

            skips = true;
            if (ReachesSplit(successor))
            {
                return BarrierDivergence.Condition;
            }
        }

        // [WHY] A barrier in the region that every successor reaches can only come from a graph that the structured
        // statements of C# do not build; it is not taken for an exit
        return skips ? BarrierDivergence.ExitOnly : BarrierDivergence.Condition;
    }

    private bool Reaches(int from, int target, int avoid) =>
        Search(from, (Target: target, Avoid: avoid), static (s, node) => node == s.Target,
            static (s, node) => node != s.Avoid);

    private bool ReachesSplit(int from) =>
        Search(from, state, static (s, node) => s.NodeAt(node).HasSplit, static (_, _) => true);

    // A depth-first search from 'from' for a node that found accepts, through the successors that follow accepts
    private bool Search<T>(int from, T context, Func<T, int, bool> found, Func<T, int, bool> follows)
    {
        var reached = new bool[state.NodeCount];
        var stack = ImmutableArray.CreateBuilder<int>();
        stack.Add(from);
        reached[from] = true;
        while (stack.Count > 0)
        {
            var node = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (found(context, node))
            {
                return true;
            }

            foreach (var successor in state.Successors[node])
            {
                if (follows(context, successor) && !reached[successor])
                {
                    reached[successor] = true;
                    stack.Add(successor);
                }
            }
        }

        return false;
    }

    // A callee in this compilation: reported where the call is when the callee diverges with these arguments and not
    // with uniform ones (the callee reports that case itself), or when the threads that leave the callee early meet the
    // others at a split point after the call
    private BarrierReport GetLocalCallSiteReport(SyntaxNode call, IMethodSymbol definition,
        MethodDeclarationSyntax declaration, ArgumentMasks masks)
    {
        var summary = state.Reader.GetCalleeSummary(definition, declaration, masks);
        var uniform = state.Reader.GetCalleeSummary(definition, declaration, default);
        return summary.Barrier switch
        {
            BarrierDivergence.Condition when uniform.Barrier != BarrierDivergence.Condition =>
                new BarrierReport(BarrierDivergence.Condition, summary.Certain),
            BarrierDivergence.ExitOnly when ReachesSplitAfter(call) =>
                new BarrierReport(BarrierDivergence.Condition, summary.Certain),
            _ => default
        };
    }

    // [WHY] Another assembly records one bit per parameter, not the certainty: a call site is a warning at most
    private BarrierReport GetImportedCallSiteReport(SyntaxNode call, IMethodSymbol callee, ArgumentMasks masks)
    {
        if (!DeviceExportTable.TryFindFunction((callee.ReducedFrom ?? callee).OriginalDefinition,
                state.Model.Compilation, out var imported))
        {
            return default;
        }

        var variant = ExportBits(masks.Block);
        var record = imported.Record;
        if ((record.DivergentConditionParameters & variant) != 0)
        {
            return new BarrierReport(BarrierDivergence.Condition, Certain: false);
        }

        return (record.HasExitOnlyBarrier || (record.DivergentExitOnlyParameters & variant) != 0) &&
               ReachesSplitAfter(call)
            ? new BarrierReport(BarrierDivergence.Condition, Certain: false)
            : default;
    }

    // The export bits of a parameter mask: parameters past the 32nd share the last bit
    private static int ExportBits(ulong mask)
    {
        var bits = (int)(mask & 0x7FFF_FFFF);
        return (mask >> 31) != 0 ? bits | int.MinValue : bits;
    }

    // What a barrier-reaching call outside a divergent region adds to the summary of the method that makes it
    private BarrierReport GetCallComposition(SyntaxNode call)
    {
        if (!state.Recorder.Sites.TryGetValue(call, out var site) ||
            site.Call is not { } invocation ||
            !state.Reader.TryGetDeviceFunction(invocation.TargetMethod, out var definition, out var declaration))
        {
            return default;
        }

        var summary = state.Reader.GetCalleeSummary(definition, declaration, GetSiteMasks(call));
        return summary.Barrier == BarrierDivergence.ExitOnly && ReachesSplitAfter(call)
            ? new BarrierReport(BarrierDivergence.Condition, summary.Certain)
            : new BarrierReport(summary.Barrier, summary.Certain);
    }

    // The stronger divergence; the certainty of the branches that give it
    private static BarrierReport Combine(BarrierReport current, BarrierReport next) =>
        next.Divergence > current.Divergence ? next :
        next.Divergence == current.Divergence && next.Divergence != BarrierDivergence.None
            ? current with { Certain = current.Certain || next.Certain }
            : current;

    // Whether a split point follows the call: later in its node, or on any path from the node
    private bool ReachesSplitAfter(SyntaxNode call)
    {
        if (!state.Recorder.SplitsAt.TryGetValue(call, out var nodes))
        {
            return false;
        }

        foreach (var node in nodes)
        {
            var splits = state.NodeAt(node).Splits;
            if (splits.IndexOf(call) < splits.Count - 1)
            {
                return true;
            }

            foreach (var successor in state.Successors[node])
            {
                if (ReachesSplit(successor))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The argument masks of a barrier-reaching call: the variance from the solved state, the identity from the probes
    private ArgumentMasks GetSiteMasks(SyntaxNode call)
    {
        RecordAllSiteMasks();
        var masks = state.SiteMasks != null && state.SiteMasks.TryGetValue(call, out var recorded) ? recorded : default;
        return masks with { Identity = state.Prober.GetIdentityMask(call) & masks.Block };
    }

    // Records the argument masks of every barrier-reaching call, from the solved state
    private void RecordAllSiteMasks()
    {
        if (state.SiteMasks != null)
        {
            return;
        }

        state.SiteMasks = new Dictionary<SyntaxNode, ArgumentMasks>();
        var current = new ThreadVariance[state.Recorder.ValueCount];
        for (var node = 0; node < state.NodeCount; node++)
        {
            state.Solver.RunNode(node, current);
        }
    }
}
