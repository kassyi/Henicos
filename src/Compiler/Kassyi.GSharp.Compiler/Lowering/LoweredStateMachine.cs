namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>A kernel or device function body lowered into a switch-based state machine, with the contents of its frame.</summary>
/// <param name="LoweredBody">The state machine body.</param>
/// <param name="ContextFields">Locals hoisted into the frame because they live across a suspension.</param>
/// <param name="CallSites">
/// Calls to device functions that may suspend, in frame index order. Call <c>i</c> keeps the callee's frame in the field
/// <c>__gsharp_call_frame_{i}</c>.
/// </param>
/// <param name="CopiedParameters">
/// By-value parameters of a device function that the body assigns. They are copied into the frame on the first entry,
/// because the caller passes the original value again on every resume.
/// </param>
public sealed record LoweredStateMachine(
    BlockSyntax LoweredBody,
    IReadOnlyList<VariableDeclaratorSyntax> ContextFields,
    IReadOnlyList<InvocationExpressionSyntax> CallSites,
    IReadOnlyList<string> CopiedParameters);
