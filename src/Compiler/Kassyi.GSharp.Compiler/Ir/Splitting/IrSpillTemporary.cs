namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>A temporary that <see cref="IrSpillRewriter"/> created.</summary>
/// <param name="Variable">The temporary.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Origin">
/// The node whose value it holds: the operand, the call, the conditional or the compound target.
/// </param>
internal readonly record struct IrSpillTemporary(IrVariable Variable, IrTemporaryKind Kind, IrExpression Origin);
