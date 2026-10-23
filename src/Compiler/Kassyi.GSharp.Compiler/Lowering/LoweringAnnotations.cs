namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The syntax annotations that the lowering passes attach to the tree they produce. The emitters
/// (<c>StatementTranspilers</c>, <c>ManagedCSharpTranspiler</c>) and the semantic model lookups read them back, so the
/// writer and every reader refer to these constants.
/// </summary>
public static class LoweringAnnotations
{
    /// <summary>Annotation kind that records the span of the original node a lowered node was derived from.</summary>
    public const string OriginalSpanAnnotationKind = "GSharpOriginalSpan";

    /// <summary>Annotation kind that marks a call to a device function that reaches a barrier (see <see cref="SuspensionAnalysis"/>).</summary>
    public const string SuspendingCallAnnotationKind = "GSharpSuspendingCall";

    /// <summary>
    /// Data of a <see cref="SuspendingCallAnnotationKind"/> annotation on a call whose callee returns a value. The state
    /// machine always stores that value, even when the caller discards it.
    /// </summary>
    public const string ValueReturningCallAnnotationData = "Value";

    /// <summary>
    /// Annotation kind that marks a <c>return</c> the state machine inserts to suspend (after storing the next state), as
    /// opposed to a <c>return</c> of the user's code. Emitters leave it as a plain suspension instead of finishing the
    /// thread or completing the device function call.
    /// </summary>
    public const string StateMachineReturnAnnotationKind = "StateMachineReturn";
}
