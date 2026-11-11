namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// A <c>ref</c> local that the lowering hoisted into the frame (<see cref="LoweredStateMachine.HeldRefLocals"/>).
/// </summary>
/// <param name="Declarator">Its declarator in the lowered body, with its original-span annotation.</param>
/// <param name="IsReportedLive">
/// Whether the analyzer reports it as GSH0039: a declaration statement's local that is live after a split point. The
/// others (rebound, used after a label that starts a state, a <c>ref</c> foreach variable) are reported by the
/// generator.
/// </param>
public readonly record struct HeldRefLocal(VariableDeclaratorSyntax Declarator, bool IsReportedLive);
