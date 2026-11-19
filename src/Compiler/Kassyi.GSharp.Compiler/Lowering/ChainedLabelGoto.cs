namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// A <c>goto</c> to the inner label of chained labels (<c>second</c> of <c>first: second: s</c>) from another state of
/// the CPU state machine than the label (<see cref="LoweredStateMachine.GotosToChainedLabelsAcrossStates"/>, GSH0121).
/// </summary>
/// <param name="Goto">The <c>goto</c> in the lowered body, with its original-span annotation.</param>
/// <param name="Label">The label it names.</param>
/// <param name="OuterLabel">The first label of the chain, which names the same statement and can start a state.</param>
public readonly record struct ChainedLabelGoto(GotoStatementSyntax Goto, string Label, string OuterLabel);
