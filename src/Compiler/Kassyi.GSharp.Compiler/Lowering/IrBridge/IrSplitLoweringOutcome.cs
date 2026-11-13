using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// What <see cref="IrSplitLoweringPass"/> did with one statement of the source whose IR statements hold a split.
/// </summary>
/// <param name="Source">The span of the statement of the source (or of the expression body).</param>
/// <param name="Fallback">
/// Why the plan left the statement to the syntax passes; <see cref="IrSpillFallback.None"/> when it was planned.
/// </param>
/// <param name="Skip">
/// Why the planned statement was not written out; <see cref="IrSpillApplySkip.None"/> when it was, or when it has a
/// fallback.
/// </param>
/// <param name="Plans">The plans of the IR statements of the statement, in the order they run.</param>
/// <param name="Printed">The statements written in its place; empty when it was left to the syntax passes.</param>
internal sealed record IrSplitLoweringOutcome(TextSpan Source, IrSpillFallback Fallback, IrSpillApplySkip Skip,
    ImmutableArray<IrStatementSpillPlan> Plans, ImmutableArray<StatementSyntax> Printed)
{
    /// <summary>Whether the statement was written out by the split pass of the IR.</summary>
    public bool IsLowered => Fallback == IrSpillFallback.None && Skip == IrSpillApplySkip.None;
}
