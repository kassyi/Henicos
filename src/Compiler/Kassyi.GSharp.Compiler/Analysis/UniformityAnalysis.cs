using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides, on the control flow graph of a kernel or device function body, which values and which branches may differ
/// between threads, which code only some threads reach, and whether a barrier is divergent (GSH0037, GSH0077; #446,
/// #447).
/// </summary>
/// <remarks>
/// <para>
/// Values: a forward data flow over the locals, parameters and flow captures, with one <see cref="ThreadVariance"/> per
/// value. A value is thread-variant when it depends on <c>ThreadIndex</c>, on an intrinsic whose result differs between
/// threads (<see cref="SyncIntrinsicClassifier"/>), on a device function whose result is thread-variant for the
/// variance of its arguments (analyzed in the same way, cached per argument mask), on another method called with a
/// thread-variant argument, or on a parameter of a lambda or local function, whose callers are not followed. It varies
/// between blocks only when it depends on <c>BlockIndex</c>. An assignment sets the variance of its target from its
/// value, and so also makes it uniform again (#447); a deconstruction assigns each element (#446-2). An assignment
/// inside a lambda or local function to a local declared outside it only adds to the variance, as the body may run at
/// any time.
/// </para>
/// <para>
/// Branches: a branch is divergent when its condition is thread-variant. The code that only some threads reach is its
/// influence region: what the successors of the branch reach before its immediate post-dominator
/// (<see cref="PostDominatorTree"/>, with a virtual exit, to which the end of the method, a <c>throw</c> and any part
/// that cannot reach the end are connected). A loop whose exit condition is thread-variant puts its whole body in the
/// region. Where paths from two successors of a divergent branch meet again (in its region or at its post-dominator,
/// not coming back through the branch), a value written in the region may differ between threads (LLVM's sync
/// dependence; after a loop with a thread-variant exit, its temporal divergence). A value written after an early
/// <c>return</c> is not: the threads that returned never meet the others. The values, the regions and the meeting
/// points are solved together until none grows (<see cref="UniformitySolver"/>).
/// </para>
/// <para>
/// Barriers: <see cref="BarrierClassifier"/>.
/// </para>
/// <para>
/// The graph is the spliced flow graph of the method (<see cref="SplicedFlowBuilder"/>), on which
/// <see cref="UniformityRecorder"/> records what each node writes and evaluates. The parts share the state of one run
/// (<see cref="UniformityState"/>), which this facade owns.
/// </para>
/// <para>
/// [WHY] Two assumptions keep the analysis within one method, and both may miss a divergent branch: a parameter of a
/// kernel or device function is uniform unless the caller's argument mask says otherwise, and an element read with a
/// uniform index is uniform (memory is not followed). Fields and array elements are not values of the analysis.
/// </para>
/// </remarks>
internal sealed class UniformityAnalysis
{
    private readonly UniformityState _state;

    private UniformityAnalysis(UniformityState state) => _state = state;

    /// <summary>
    /// Analyzes the spliced graph of the body of <paramref name="method"/>, recorded by <paramref name="recorder"/>, with
    /// uniform parameters.
    /// </summary>
    public static UniformityAnalysis Analyze(SplicedFlowGraph graph, UniformityRecorder recorder, SemanticModel model,
        IMethodSymbol method) =>
        Run(new UniformityState(model, method, graph, recorder,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));

    /// <summary>
    /// Analyzes <paramref name="graph"/>, the body of <paramref name="method"/>, for a call whose arguments vary as
    /// <paramref name="masks"/> says.
    /// </summary>
    public static UniformityAnalysis AnalyzeBody(ControlFlowGraph graph, SemanticModel model, IMethodSymbol method,
        ArgumentMasks masks, HashSet<IMethodSymbol> calleesInProgress)
    {
        var recorder = new UniformityRecorder(method, masks);
        var spliced = SplicedFlowBuilder.Build(graph, model, ImmutableArray.Create<IFlowRecorder>(recorder));
        return Run(new UniformityState(model, method, spliced, recorder, calleesInProgress));
    }

    /// <summary>Whether only some threads of a block reach the call or object creation <paramref name="node"/>.</summary>
    public bool IsReachedByPartOfBlock(SyntaxNode node) =>
        _state.Recorder.NodesOf.TryGetValue(node, out var nodes) &&
        nodes
            .AsStackEnumerable()
            .Any(_state.Region, static (index, region) => region[index] == ThreadVariance.Block);

    /// <summary>Whether <paramref name="expression"/> may evaluate to different values in different threads of a block.</summary>
    public bool IsThreadVariant(SyntaxNode expression)
    {
        if (_state.Observed == null)
        {
            _state.Solver.Observe();
        }

        return _state.Observed != null &&
               _state.Observed.TryGetValue(expression, out var variance) &&
               variance == ThreadVariance.Block;
    }

    /// <inheritdoc cref="BarrierClassifier.GetBarrierDivergence"/>
    public BarrierDivergence GetBarrierDivergence(SyntaxNode barrier, SyncScope scope) =>
        _state.Barriers.GetBarrierDivergence(barrier, scope);

    /// <inheritdoc cref="BarrierClassifier.GetCallSiteDivergence"/>
    public BarrierDivergence GetCallSiteDivergence(SyntaxNode call) => _state.Barriers.GetCallSiteDivergence(call);

    /// <inheritdoc cref="BarrierClassifier.GetExitBranches"/>
    public ImmutableArray<SyntaxNode> GetExitBranches(SyntaxNode barrier, SyncScope scope) =>
        _state.Barriers.GetExitBranches(barrier, scope);

    /// <inheritdoc cref="BarrierClassifier.IsExitOnlyCall"/>
    public bool IsExitOnlyCall(SyntaxNode call) => _state.Barriers.IsExitOnlyCall(call);

    /// <inheritdoc cref="BarrierClassifier.GetExportedDivergence"/>
    public static ExportedBarrierDivergence GetExportedDivergence(IMethodSymbol method, Compilation compilation) =>
        BarrierClassifier.GetExportedDivergence(method, compilation);

    /// <inheritdoc cref="UniformityValueReader.Summarize"/>
    public CalleeVariance Summarize() => _state.Reader.Summarize();

    /// <inheritdoc cref="UniformityValueReader.GetCalleeSummary"/>
    public CalleeVariance GetCalleeSummary(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        ArgumentMasks masks) =>
        _state.Reader.GetCalleeSummary(callee, declaration, masks);

    private static UniformityAnalysis Run(UniformityState state)
    {
        state.Solver.Solve();
        return new UniformityAnalysis(state);
    }
}
