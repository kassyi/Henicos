namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The rewritten condition of a <see langword="while"/> loop whose condition holds a split: the statements that
/// evaluate its operands and splits before the branch, the boolean condition expression that reads their
/// temporaries, and the temporaries declared.
/// </summary>
/// <param name="statements">The spill statements to emit after the loop start label.</param>
/// <param name="condition">The condition expression reading the spilled temporaries.</param>
/// <param name="temporaries">The temporaries declared by <paramref name="statements"/>.</param>
internal sealed class IrLoweredCondition(ImmutableArray<IrStatement> statements, IrExpression condition,
    ImmutableArray<IrSpillTemporary> temporaries)
{
    /// <summary>The spill statements to emit after the loop start label.</summary>
    public ImmutableArray<IrStatement> Statements { get; } = statements;

    /// <summary>The condition expression reading the spilled temporaries.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>The temporaries declared by <see cref="Statements"/>.</summary>
    public ImmutableArray<IrSpillTemporary> Temporaries { get; } = temporaries;
}
