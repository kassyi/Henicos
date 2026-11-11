namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// One step that the split pass puts before a statement, in the C# order of evaluation: <see cref="IrSpillValue"/>,
/// <see cref="IrSpillSplit"/> or <see cref="IrSpillConditional"/>.
/// </summary>
/// <param name="Node">The operand spilled, the call that splits, or the conditional expression.</param>
internal abstract record IrSpillStep(IrExpression Node);
