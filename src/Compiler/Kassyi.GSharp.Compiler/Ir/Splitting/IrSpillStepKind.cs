namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>What one step of an <see cref="IrStatementSpillPlan"/> does before the statement.</summary>
internal enum IrSpillStepKind
{
    /// <summary>The value of <see cref="IrSpillStep.Node"/> is evaluated once into a temporary.</summary>
    Spill,

    /// <summary>
    /// The call <see cref="IrSpillStep.Node"/> splits the state machine here; its value, if any, is its temporary.
    /// </summary>
    Split,

    /// <summary>
    /// The <c>?:</c>, <c>&amp;&amp;</c>, <c>||</c> or <c>??</c> <see cref="IrSpillStep.Node"/>, whose conditionally
    /// evaluated operand holds a split, becomes an <see langword="if"/> that assigns a temporary; each branch has its
    /// own steps (<see cref="IrSpillStep.Branches"/>).
    /// </summary>
    Conditional
}
