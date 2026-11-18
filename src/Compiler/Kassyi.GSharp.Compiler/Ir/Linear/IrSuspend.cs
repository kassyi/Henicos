namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// Suspends the state machine at a barrier or warp publish and sets the next state to resume at.
/// </summary>
/// <param name="kind">Why the state machine suspends.</param>
/// <param name="resumeState">The state number at which execution resumes.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrSuspend(IrSuspendKind kind, int resumeState, IrSource source) : IrStatement(source)
{
    /// <summary>Why the state machine suspends.</summary>
    public IrSuspendKind Kind { get; } = kind;

    /// <summary>The state number at which execution resumes.</summary>
    public int ResumeState { get; } = resumeState;
}
