namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>One operand of a form that goes into a temporary, and what the temporary holds.</summary>
/// <param name="OperandIndex">The position of the operand in the C# order of evaluation of the form.</param>
/// <param name="Kind">What the temporary holds.</param>
internal readonly record struct IrSequenceTempPlan(int OperandIndex, IrSequenceTempKind Kind);
