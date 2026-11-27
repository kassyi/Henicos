namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>A kernel or device function body lowered into a switch-based state machine, with the contents of its frame.</summary>
/// <param name="LoweredBody">The state machine body.</param>
/// <param name="ContextFields">Locals hoisted into the frame because they live across a suspension.</param>
/// <param name="CallSites">
/// Calls to device functions that may suspend, in frame index order. Call <c>i</c> keeps the callee's frame in the field
/// <c>__gsharp_call_frame_{i}</c>.
/// </param>
/// <param name="CopiedParameters">
/// By-value parameters of a kernel or device function that the body assigns. They are copied into the frame on the first
/// entry, because the caller (the launcher, for a kernel) passes the original value again on every resume. Empty when
/// the body has no split point, since it then runs once.
/// </param>
/// <param name="RegionCount">
/// The highest region number assigned by pre-lowering expansions so that subsequent inline expansions can continue from
/// a disjoint counter.
/// </param>
/// <param name="WarpSiteCount">
/// The warp collectives of the body (shuffles, votes, reductions, matches, <c>Gpu.SyncWarp</c>), numbered from 0. The
/// frame counts how often the thread reached each one, so that the lanes of a warp meet at the same instance of a call
/// that a loop repeats.
/// </param>
public sealed record LoweredStateMachine(
    BlockSyntax LoweredBody,
    IReadOnlyList<VariableDeclaratorSyntax> ContextFields,
    IReadOnlyList<InvocationExpressionSyntax> CallSites,
    IReadOnlyList<string> CopiedParameters,
    int RegionCount = 0,
    int WarpSiteCount = 0)
{
    /// <summary>
    /// The <c>ref</c> locals among <see cref="ContextFields"/>. Neither frame can hold one (a managed class has no ref
    /// fields, CS9059; the native frame would hold a copy of the target), so the managed state machine of such a body
    /// only throws, and each one is an error: GSH0039 from the analyzer when
    /// <see cref="HeldRefLocal.IsReportedLive"/>, otherwise GSH0003 from the generator (#1640).
    /// </summary>
    public ImmutableArray<HeldRefLocal> HeldRefLocals { get; init; } = ImmutableArray<HeldRefLocal>.Empty;

    /// <summary>
    /// The span locals among <see cref="ContextFields"/> that the CPU state machine declares again on every resume and
    /// that a state reads before it writes (<see cref="SpanAcrossStates"/>), without the ones that the analyzer
    /// reports. Each one is GSH0039 from the generator.
    /// </summary>
    public ImmutableArray<VariableDeclaratorSyntax> SpansAcrossStates { get; init; } =
        ImmutableArray<VariableDeclaratorSyntax>.Empty;

    /// <summary>
    /// The gotos to an inner label of chained labels from another state (<see cref="StatePlan"/>), which
    /// <see cref="StateMachineLowerer"/> leaves as gotos into another case block. The generator reports
    /// each one that does not cross a barrier, as GSH0121 (<see cref="ChainedLabelJumpChecks.FindReported"/>; GSH0040 is
    /// the analyzer's); the managed body of such a method only throws where it runs (no goto).
    /// </summary>
    public ImmutableArray<ChainedLabelGoto> GotosToChainedLabelsAcrossStates { get; init; } =
        ImmutableArray<ChainedLabelGoto>.Empty;
}
