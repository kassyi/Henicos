namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The call <see cref="IrSpillStep.Node"/> splits the state machine here; its value, if any, is its temporary.
/// </summary>
/// <param name="Node">The call.</param>
internal sealed record IrSpillSplit(IrExpression Node) : IrSpillStep(Node);
