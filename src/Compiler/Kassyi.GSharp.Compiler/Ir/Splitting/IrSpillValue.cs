namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>The value of <see cref="IrSpillStep.Node"/> is evaluated once into a temporary.</summary>
/// <param name="Node">The operand.</param>
/// <param name="Role">Why it is evaluated before the split.</param>
/// <param name="ForcedByConditional">
/// Whether the spill exists only because a split inside a conditionally evaluated operand comes after it: no operand
/// between it and that split holds a split that always runs.
/// </param>
internal sealed record IrSpillValue(IrExpression Node, IrSpillRole Role, bool ForcedByConditional)
    : IrSpillStep(Node);
