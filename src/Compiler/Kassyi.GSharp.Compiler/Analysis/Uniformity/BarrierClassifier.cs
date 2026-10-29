using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides, for a <see cref="UniformityAnalysis"/>, whether a barrier or barrier-reaching call is divergent: in the
/// influence region of a divergent branch of the method, or because of the callee and the variance of the arguments
/// (§3.2.4), and what the barriers of the method give its callers.
/// </summary>
/// <remarks>
/// <para>
/// A barrier in the influence region of a divergent branch is skipped by the threads of a successor of the branch when
/// some path from that successor reaches the immediate post-dominator of the branch (or the end of the method) without
/// passing the barrier and without coming back through the branch, while another successor reaches the barrier: the
/// branch then decides which threads reach it. A successor from which no other successor reaches the barrier skips it
/// only on a later, uniform branch, which this branch does not decide. The other successors of <c>||</c>,
/// <c>&amp;&amp;</c> and <c>?:</c> may still reach the barrier on another path; that does not make the threads on the
/// skipping path reach it. The barrier is <see cref="BarrierDivergence.Condition"/> when the threads on a skipping path
/// can reach a point where the state machine splits and from which they can go on to the post-dominator or the end
/// without the barrier, so that they wait there while the others wait at the barrier (another split point, or the
/// barrier itself again after the paths meet: the next iteration of a loop); otherwise they only leave the method,
/// <see cref="BarrierDivergence.ExitOnly"/>.
/// </para>
/// <para>
/// A block barrier is divergent only on a branch that varies within a block; a grid barrier also on one that varies
/// between blocks. No divergent branch is certain at compile time: which threads take it depends on the launch (a block
/// of one thread, or one whose X width is 1), which the compiler does not know (semantics charter §3). The divergence is
/// a warning (GSH0083, GSH0084); the barriers that do diverge are caught at run time.
/// </para>
/// </remarks>
internal sealed class BarrierClassifier(UniformityState state)
{
    /// <summary>
    /// Whether only some of the threads that must reach the barrier or barrier-reaching call <paramref name="barrier"/>
    /// reach it because a divergent branch of this method decides it.
    /// </summary>
    public BarrierDivergence GetBarrierDivergence(SyntaxNode barrier, SyncScope scope)
    {
        if (!state.Recorder.SplitsAt.TryGetValue(barrier, out var nodes))
        {
            return BarrierDivergence.None;
        }

        var result = BarrierDivergence.None;
        foreach (var node in nodes)
        {
            for (var branch = 0; branch < state.Branch.Length; branch++)
            {
                if (Diverges(state.Branch[branch], scope) && state.PostDominators.GetRegion(branch).Contains(node))
                {
                    result = Combine(result, Classify(branch, node));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the call <paramref name="call"/> to a device function that reaches a block barrier makes only some
    /// threads reach a barrier inside the callee, given which arguments vary (§3.2.4).
    /// </summary>
    public BarrierDivergence GetCallSiteDivergence(SyntaxNode call)
    {
        if (!state.Recorder.Sites.TryGetValue(call, out var site) || site.Call is not { } invocation)
        {
            return BarrierDivergence.None;
        }

        var masks = GetSiteMasks(call);
        var callee = invocation.TargetMethod;
        return state.Reader.TryGetDeviceFunction(callee, out var definition, out var declaration)
            ? GetLocalCallSiteDivergence(call, definition, declaration, masks)
            : GetImportedCallSiteDivergence(call, callee, masks);
    }

    /// <summary>
    /// The conditions of the divergent branches that make <paramref name="barrier"/> exit only (§3.2.3). Empty unless the
    /// barrier is <see cref="BarrierDivergence.ExitOnly"/> as a whole.
    /// </summary>
    public ImmutableArray<SyntaxNode> GetExitBranches(SyntaxNode barrier, SyncScope scope)
    {
        if (GetBarrierDivergence(barrier, scope) != BarrierDivergence.ExitOnly ||
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
        GetBarrierDivergence(call, SyncScope.Block) == BarrierDivergence.None &&
        GetCallComposition(call) == BarrierDivergence.ExitOnly;

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
                new ArgumentMasks(ArgumentMasks.Bit(parameter.Ordinal), 0));
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
    public BarrierDivergence SummarizeBarriers()
    {
        var result = BarrierDivergence.None;
        foreach (var pair in state.Recorder.Sites)
        {
            var divergence = GetBarrierDivergence(pair.Key, pair.Value.Scope);
            if (divergence == BarrierDivergence.None && pair.Value.Call != null)
            {
                divergence = GetCallComposition(pair.Key);
            }

            result = Combine(result, divergence);
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
            ? new ArgumentMasks(seen.Block | masks.Block, seen.Grid | masks.Grid)
            : masks;
    }

    private static bool Diverges(ThreadVariance branch, SyncScope scope) =>
        scope == SyncScope.Grid ? branch != ThreadVariance.Uniform : branch == ThreadVariance.Block;

    // A barrier at node in the influence region of branch: Condition when the threads on a path that skips it can reach a
    // split point, ExitOnly when they only leave the method
    private BarrierDivergence Classify(int branch, int node)
    {
        var stop = state.PostDominators.GetImmediatePostDominator(branch);
        var successors = state.Successors[branch];
        var skips = false;
        foreach (var successor in successors)
        {
            if (!Skips(successor, node, branch, stop) || !OtherReaches(successors, successor, node, branch))
            {
                continue;
            }

            skips = true;
            if (ReachesMeetingSplit(successor, node, stop) ||
                (stop < state.NodeCount && Reaches(stop, node, avoid: -1)))
            {
                return BarrierDivergence.Condition;
            }
        }

        // [WHY] A barrier in the region that no successor can skip can only come from a graph that the structured
        // statements of C# do not build; it is not taken for an exit
        return skips ? BarrierDivergence.ExitOnly : BarrierDivergence.Condition;
    }

    // Whether a path from successor reaches stop (the immediate post-dominator of branch, or the end of the method when
    // stop is the virtual exit) without passing node or coming back through branch
    private bool Skips(int successor, int node, int branch, int stop) =>
        successor != node &&
        Search(successor, (Node: node, Branch: branch, Stop: stop, State: state),
            static (s, n) => n == s.Stop || (s.Stop >= s.State.NodeCount && s.State.Successors[n].Length == 0),
            static (s, n) => n != s.Node && n != s.Branch);

    private bool Reaches(int from, int target, int avoid) =>
        Search(from, (Target: target, Avoid: avoid), static (s, node) => node == s.Target,
            static (s, node) => node != s.Avoid);

    private bool ReachesSplit(int from) =>
        Search(from, state, static (s, node) => s.NodeAt(node).HasSplit, static (_, _) => true);

    // Whether a successor of branch other than successor reaches node without coming back through branch: only then
    // does branch decide whether the threads on successor reach node
    private bool OtherReaches(ImmutableArray<int> successors, int successor, int node, int branch)
    {
        foreach (var other in successors)
        {
            if (other != successor && Reaches(other, node, avoid: branch))
            {
                return true;
            }
        }

        return false;
    }

    // A split point that the threads on a path from 'from' reach without passing node (the barrier that they skip), and
    // from which they can go on to stop or to the end of the method without passing node: they wait there while the
    // others wait at node. A split from which every path leads to node only comes before it
    private bool ReachesMeetingSplit(int from, int node, int stop) =>
        from != node &&
        Search(from, (Classifier: this, Node: node, Stop: stop),
            static (s, n) => s.Classifier.IsMeetingSplit(n, s.Node, s.Stop),
            static (s, n) => n != s.Node);

    private bool IsMeetingSplit(int split, int node, int stop) =>
        state.NodeAt(split).HasSplit && ReachesStopAvoiding(split, node, stop);

    // Whether a path from 'from' reaches stop or the end of the method without passing node
    private bool ReachesStopAvoiding(int from, int node, int stop) =>
        Search(from, (Node: node, Stop: stop, State: state),
            static (s, n) => n == s.Stop || s.State.Successors[n].Length == 0,
            static (s, n) => n != s.Node);

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
    private BarrierDivergence GetLocalCallSiteDivergence(SyntaxNode call, IMethodSymbol definition,
        MethodDeclarationSyntax declaration, ArgumentMasks masks)
    {
        var summary = state.Reader.GetCalleeSummary(definition, declaration, masks);
        var uniform = state.Reader.GetCalleeSummary(definition, declaration, default);
        return summary.Barrier switch
        {
            BarrierDivergence.Condition when uniform.Barrier != BarrierDivergence.Condition => BarrierDivergence.Condition,
            BarrierDivergence.ExitOnly when ReachesSplitAfter(call) => BarrierDivergence.Condition,
            _ => BarrierDivergence.None
        };
    }

    // A callee of another assembly, judged from the one bit per parameter that its export record carries
    private BarrierDivergence GetImportedCallSiteDivergence(SyntaxNode call, IMethodSymbol callee, ArgumentMasks masks)
    {
        if (!DeviceExportTable.TryFindFunction((callee.ReducedFrom ?? callee).OriginalDefinition,
                state.Model.Compilation, out var imported))
        {
            return BarrierDivergence.None;
        }

        var variant = ExportBits(masks.Block);
        var record = imported.Record;
        if ((record.DivergentConditionParameters & variant) != 0)
        {
            return BarrierDivergence.Condition;
        }

        return (record.HasExitOnlyBarrier || (record.DivergentExitOnlyParameters & variant) != 0) &&
               ReachesSplitAfter(call)
            ? BarrierDivergence.Condition
            : BarrierDivergence.None;
    }

    // The export bits of a parameter mask: parameters past the 32nd share the last bit
    private static int ExportBits(ulong mask)
    {
        var bits = (int)(mask & 0x7FFF_FFFF);
        return (mask >> 31) != 0 ? bits | int.MinValue : bits;
    }

    // What a barrier-reaching call outside a divergent region adds to the summary of the method that makes it
    private BarrierDivergence GetCallComposition(SyntaxNode call)
    {
        if (!state.Recorder.Sites.TryGetValue(call, out var site) ||
            site.Call is not { } invocation ||
            !state.Reader.TryGetDeviceFunction(invocation.TargetMethod, out var definition, out var declaration))
        {
            return BarrierDivergence.None;
        }

        var barrier = state.Reader.GetCalleeSummary(definition, declaration, GetSiteMasks(call)).Barrier;
        return barrier == BarrierDivergence.ExitOnly && ReachesSplitAfter(call) ? BarrierDivergence.Condition : barrier;
    }

    // The stronger divergence
    private static BarrierDivergence Combine(BarrierDivergence current, BarrierDivergence next) =>
        next > current ? next : current;

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

    // The argument masks of a barrier-reaching call, from the solved state
    private ArgumentMasks GetSiteMasks(SyntaxNode call)
    {
        RecordAllSiteMasks();
        return state.SiteMasks != null && state.SiteMasks.TryGetValue(call, out var recorded) ? recorded : default;
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
