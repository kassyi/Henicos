using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// Calls a suspending device function in a state machine:
/// <c>if (!__gsharp_resume(&lt;Call&gt;, &lt;CallFrame&gt;[, &lt;ResultTarget&gt;]))</c>
/// <c>{ __gsharp_state = &lt;State&gt;; return; }</c>.
/// </summary>
/// <param name="call">The suspending device function call.</param>
/// <param name="callFrame">The zero-based call-frame slot index.</param>
/// <param name="resultTarget">The variable that receives the call result, or <see langword="null"/> for void.</param>
/// <param name="state">The state number that this statement starts and re-enters on resume.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrResumeCall(IrCall call, int callFrame, IrVariableReference? resultTarget, int state,
    IrSource source) : IrStatement(source)
{
    /// <summary>The suspending device function call.</summary>
    public IrCall Call { get; } = call;

    /// <summary>The zero-based call-frame slot index.</summary>
    public int CallFrame { get; } = callFrame;

    /// <summary>
    /// The variable that receives the call result (with <see cref="IrVariableReference.IsDeclaration"/> when declared
    /// here), or <see langword="null"/>.
    /// </summary>
    public IrVariableReference? ResultTarget { get; } = resultTarget;

    /// <summary>The state number that this statement starts and re-enters on resume.</summary>
    public int State { get; } = state;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrResumeCall Update(IrCall call, IrVariableReference? resultTarget) =>
        call == Call && resultTarget == ResultTarget
            ? this
            : KeepSpelling(new IrResumeCall(call, CallFrame, resultTarget, State, Source));
}
