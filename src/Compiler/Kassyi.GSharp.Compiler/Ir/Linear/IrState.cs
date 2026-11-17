namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>One numbered state in an <see cref="IrStateMachine"/>.</summary>
/// <param name="number">The zero-based state number.</param>
/// <param name="statements">The statements executed in this state.</param>
/// <param name="unreachableFrom">
/// The index in <paramref name="statements"/> from which statements are unreachable, or <see langword="null"/>.
/// </param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrState(int number, ImmutableArray<IrStatement> statements, int? unreachableFrom,
    IrSource source) : IrNode(source)
{
    /// <summary>The zero-based state number.</summary>
    public int Number { get; } = number;

    /// <summary>The statements executed in this state.</summary>
    public ImmutableArray<IrStatement> Statements { get; } = statements;

    /// <summary>
    /// The index in <see cref="Statements"/> from which statements are unreachable, or <see langword="null"/>.
    /// </summary>
    public int? UnreachableFrom { get; } = unreachableFrom;

    /// <summary>This state with the given statements and unreachable index; this node itself when unchanged.</summary>
    public IrState Update(ImmutableArray<IrStatement> statements, int? unreachableFrom) =>
        statements == Statements && unreachableFrom == UnreachableFrom
            ? this
            : new IrState(Number, statements, unreachableFrom, Source);
}
