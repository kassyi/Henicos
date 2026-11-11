namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The <c>?:</c>, <c>&amp;&amp;</c>, <c>||</c> or <c>??</c> <see cref="IrSpillStep.Node"/>, whose conditionally
/// evaluated operand holds a split, becomes an <see langword="if"/> that assigns a temporary.
/// </summary>
/// <param name="Node">The conditional expression.</param>
/// <param name="Branches">
/// The steps of each branch: one for <c>&amp;&amp;</c>, <c>||</c> and <c>??</c>, two for <c>?:</c> (when true, when
/// false).
/// </param>
internal sealed record IrSpillConditional(IrExpression Node, ImmutableArray<ImmutableArray<IrSpillStep>> Branches)
    : IrSpillStep(Node);
