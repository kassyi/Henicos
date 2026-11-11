namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// Statements evaluated before a value, with the temporaries they declare: what the compiler makes of an expression
/// whose C# order of evaluation needs temporaries. The builder does not create it; later passes do.
/// </summary>
/// <param name="locals">The temporaries, all of <see cref="IrVariableKind.Temporary"/>.</param>
/// <param name="sideEffects">The statements, in order.</param>
/// <param name="value">The value of the sequence.</param>
/// <param name="category">How the holder uses the value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrSequence(ImmutableArray<IrVariable> locals, ImmutableArray<IrStatement> sideEffects,
    IrExpression value, IrCategory category, IrSource source)
    : IrExpression(value.Type, category, default, source)
{
    /// <summary>The temporaries.</summary>
    public ImmutableArray<IrVariable> Locals { get; } = locals;

    /// <summary>The statements, in order.</summary>
    public ImmutableArray<IrStatement> SideEffects { get; } = sideEffects;

    /// <summary>The value of the sequence.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrSequence Update(ImmutableArray<IrStatement> sideEffects, IrExpression value) =>
        sideEffects == SideEffects && value == Value
            ? this
            : new IrSequence(Locals, sideEffects, value, Category, Source);
}
