using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The compound assignment <paramref name="Compound"/>, whose target was read into a temporary before the split
/// (<see cref="IrSpillRole.CompoundRead"/>), is completed here, after the steps of its right operand and before any
/// step of what C# evaluates after it: the operator is applied to the temporary and the result stored in the target.
/// </summary>
/// <param name="Compound">The compound assignment, also <see cref="IrSpillStep.Node"/>.</param>
internal sealed record IrSpillCompoundStore(IrCompoundAssignment Compound) : IrSpillStep(Compound);
