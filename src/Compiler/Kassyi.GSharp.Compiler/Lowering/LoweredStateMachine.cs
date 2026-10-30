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
public sealed record LoweredStateMachine(
    BlockSyntax LoweredBody,
    IReadOnlyList<VariableDeclaratorSyntax> ContextFields,
    IReadOnlyList<InvocationExpressionSyntax> CallSites,
    IReadOnlyList<string> CopiedParameters,
    int RegionCount = 0);
