using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Whether only some threads reach a barrier, and whether a branch behind it certainly diverges.</summary>
/// <param name="Divergence">How the threads that skip the barrier go on.</param>
/// <param name="Certain">
/// Whether a branch that gives <paramref name="Divergence"/> certainly diverges: thread 0 and thread 1 of a block
/// (block 0 and block 1 of the grid for a grid barrier) take different sides of it. Otherwise whether it diverges depends on
/// the launch configuration or on run-time values.
/// </param>
internal readonly record struct BarrierReport(BarrierDivergence Divergence, bool Certain);

/// <summary>
/// The divergence of the barriers of a device function that a caller in another assembly needs (B7), one bit per
/// parameter by ordinal (parameters past the 32nd share the last bit).
/// </summary>
/// <param name="ConditionParameters">
/// The parameters that make a barrier diverge (<see cref="BarrierDivergence.Condition"/>) when they receive a
/// thread-variant argument, where uniform arguments do not.
/// </param>
/// <param name="ExitOnlyParameters">
/// The parameters that make the threads leave early before a barrier (<see cref="BarrierDivergence.ExitOnly"/>) when
/// they receive a thread-variant argument, where uniform arguments do not.
/// </param>
/// <param name="HasExitOnlyBarrier">
/// Whether the threads may leave early before a barrier even with uniform arguments.
/// </param>
internal readonly record struct ExportedBarrierDivergence(
    int ConditionParameters,
    int ExitOnlyParameters,
    bool HasExitOnlyBarrier);

internal sealed partial class UniformityAnalysis
{
    // The barriers and barrier-reaching calls of the graph, and the argument masks of each call once recorded
    private readonly Dictionary<SyntaxNode, BarrierSite> _sites = new();
    private Dictionary<SyntaxNode, ArgumentMasks>? _siteMasks;

    /// <summary>
    /// Whether only some of the threads that must reach the barrier or barrier-reaching call <paramref name="barrier"/>
    /// reach it because a divergent branch of this method decides it: the threads of a block for
    /// <see cref="SyncScope.Block"/>, of the grid for <see cref="SyncScope.Grid"/>. A call that does not split the
    /// state machine (a <c>Gpu.SyncGrid(body)</c> with no gap) is <see cref="BarrierDivergence.None"/>.
    /// </summary>
    public BarrierReport GetBarrierReport(SyntaxNode barrier, SyncScope scope)
    {
        if (!_splitsAt.TryGetValue(barrier, out var nodes))
        {
            return default;
        }

        var result = default(BarrierReport);
        foreach (var node in nodes)
        {
            for (var branch = 0; branch < _branch.Length; branch++)
            {
                if (!Diverges(_branch[branch], scope) || !GetRegion(branch).Contains(node))
                {
                    continue;
                }

                result = Combine(result, new BarrierReport(Classify(branch, node), IsCertain(branch, scope)));
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the call <paramref name="call"/> to a device function that reaches a block barrier makes only some
    /// threads reach a barrier inside the callee, given which arguments vary (§3.2.4): reported where the call is when
    /// the callee diverges with these arguments and not with uniform ones (the callee reports that case itself), or
    /// when the threads that leave the callee early meet the others at a split point after the call. A callee of
    /// another assembly is judged from its export record and never <see cref="BarrierReport.Certain"/>.
    /// </summary>
    public BarrierReport GetCallSiteReport(SyntaxNode call)
    {
        if (!_sites.TryGetValue(call, out var site) || site.Call is not { } invocation)
        {
            return default;
        }

        var masks = GetSiteMasks(call);
        var callee = invocation.TargetMethod;
        if (TryGetDeviceFunction(callee, out var definition, out var declaration))
        {
            var summary = GetCalleeSummary(definition, declaration, masks);
            var uniform = GetCalleeSummary(definition, declaration, default);
            return summary.Barrier switch
            {
                BarrierDivergence.Condition when uniform.Barrier != BarrierDivergence.Condition =>
                    new BarrierReport(BarrierDivergence.Condition, summary.Certain),
                BarrierDivergence.ExitOnly when ReachesSplitAfter(call) =>
                    new BarrierReport(BarrierDivergence.Condition, summary.Certain),
                _ => default
            };
        }

        if (!DeviceExportTable.TryFindFunction((callee.ReducedFrom ?? callee).OriginalDefinition, _model.Compilation,
                out var imported))
        {
            return default;
        }

        // [WHY] Another assembly records one bit per parameter, not the certainty: a call site is a warning at most
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

        var analysis = new UniformityAnalysis(model, definition, calleesInProgress: null);
        analysis.Run(graph, default);
        var uniform = analysis.Summarize().Barrier;
        var condition = 0;
        var exitOnly = 0;
        foreach (var parameter in definition.Parameters)
        {
            var summary = analysis.GetCalleeSummary(definition, declaration,
                new ArgumentMasks(ParameterBit(parameter.Ordinal), 0, 0));
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

    // The export bits of a parameter mask: parameters past the 32nd share the last bit
    private static int ExportBits(ulong mask)
    {
        var bits = (int)(mask & 0x7FFF_FFFF);
        return (mask >> 31) != 0 ? bits | int.MinValue : bits;
    }

    // Σ of the analyzed method (§3.2.4): its barriers in the region of a divergent branch, and for each
    // barrier-reaching call outside such a region, what the callee gives for its arguments. A callee whose threads
    // leave early before a barrier diverges here as soon as a split point follows the call.
    private BarrierReport SummarizeBarriers()
    {
        var result = default(BarrierReport);
        foreach (var pair in _sites)
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

    // What a barrier-reaching call outside a divergent region adds to the summary of the method that makes it
    private BarrierReport GetCallComposition(SyntaxNode call)
    {
        if (!_sites.TryGetValue(call, out var site) ||
            site.Call is not { } invocation ||
            !TryGetDeviceFunction(invocation.TargetMethod, out var definition, out var declaration))
        {
            return default;
        }

        var summary = GetCalleeSummary(definition, declaration, GetSiteMasks(call));
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
        if (!_splitsAt.TryGetValue(call, out var nodes))
        {
            return false;
        }

        foreach (var node in nodes)
        {
            var splits = _nodes[node].Splits;
            if (splits.IndexOf(call) < splits.Count - 1)
            {
                return true;
            }

            foreach (var successor in _successors[node])
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
        var masks = _siteMasks != null && _siteMasks.TryGetValue(call, out var recorded) ? recorded : default;
        return masks with { Identity = GetIdentityMask(call) & masks.Block };
    }

    // Records the argument masks of every barrier-reaching call, from the solved state
    private void RecordAllSiteMasks()
    {
        if (_siteMasks != null)
        {
            return;
        }

        _siteMasks = new Dictionary<SyntaxNode, ArgumentMasks>();
        var state = new ThreadVariance[_symbols.Count + _captures.Count];
        for (var node = 0; node < _nodes.Count; node++)
        {
            RunNode(node, state);
        }
    }

    private void RecordSiteMasks(IInvocationOperation call, ControlFlowGraph graph, ThreadVariance[] state)
    {
        if (_siteMasks == null)
        {
            return;
        }

        var masks = GetArgumentMasks(call, graph, state);
        _siteMasks[call.Syntax] = _siteMasks.TryGetValue(call.Syntax, out var seen)
            ? new ArgumentMasks(seen.Block | masks.Block, seen.Grid | masks.Grid, seen.Identity & masks.Identity)
            : masks;
    }

    /// <summary>A barrier, or a call that reaches one: the threads that must reach it, and the call itself.</summary>
    private readonly record struct BarrierSite(SyncScope Scope, IInvocationOperation? Call, ControlFlowGraph Graph);
}
