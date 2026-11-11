namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The decisions of the split pass for one method: for each statement that holds a split, what is evaluated before
/// it and in which order (<see cref="IrSpillPlanner"/>). Plain data; nothing is rewritten.
/// </summary>
/// <param name="statements">The statements that hold a split, in the order of the body.</param>
internal sealed class IrSpillPlan(ImmutableArray<IrStatementSpillPlan> statements)
{
    /// <summary>The statements that hold a split, in the order of the body.</summary>
    public ImmutableArray<IrStatementSpillPlan> Statements { get; } = statements;
}
