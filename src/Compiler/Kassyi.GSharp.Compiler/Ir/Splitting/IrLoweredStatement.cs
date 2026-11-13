namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// One statement with its split plan applied (<see cref="IrSpillRewriter"/>): the statements that evaluate what C#
/// evaluates before each split, then what is left of the statement, in the order they run.
/// </summary>
/// <param name="original">The statement of the plan.</param>
/// <param name="statements">The statements that replace it, in the order they run.</param>
/// <param name="temporaries">The temporaries the statements declare, in the order they were created.</param>
/// <param name="remaining">
/// What is left of the statement, the last of <paramref name="statements"/>; <see langword="null"/> when nothing is
/// left.
/// </param>
internal sealed class IrLoweredStatement(IrStatement original, ImmutableArray<IrStatement> statements,
    ImmutableArray<IrSpillTemporary> temporaries, IrStatement? remaining)
{
    /// <summary>The statement of the plan.</summary>
    public IrStatement Original { get; } = original;

    /// <summary>The statements that replace it, in the order they run.</summary>
    public ImmutableArray<IrStatement> Statements { get; } = statements;

    /// <summary>
    /// The temporaries the statements declare, in the order they were created (the order of their names).
    /// </summary>
    public ImmutableArray<IrSpillTemporary> Temporaries { get; } = temporaries;

    /// <summary>
    /// What is left of the statement, the last of <see cref="Statements"/>; <see langword="null"/> when nothing is left
    /// (a call whose value went to a temporary, a compound assignment completed by its store).
    /// </summary>
    public IrStatement? Remaining { get; } = remaining;
}
