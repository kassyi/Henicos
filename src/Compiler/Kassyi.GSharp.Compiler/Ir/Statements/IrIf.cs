namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>An <see langword="if"/> statement.</summary>
/// <param name="condition">The condition.</param>
/// <param name="then">The statement when the condition holds.</param>
/// <param name="else">The statement when it does not, or <see langword="null"/>.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">
/// Whether the condition is the same in every thread of a warp; unclassified when built.
/// </param>
internal sealed class IrIf(IrExpression condition, IrStatement then, IrStatement? @else, IrSource source,
    IrUniformity uniformity = IrUniformity.Unclassified) : IrStatement(source)
{
    /// <summary>The condition.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>The statement when the condition holds.</summary>
    public IrStatement Then { get; } = then;

    /// <summary>The statement when it does not, or <see langword="null"/>.</summary>
    public IrStatement? Else { get; } = @else;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrIf Update(IrExpression condition, IrStatement then, IrStatement? @else) =>
        condition == Condition && then == Then && @else == Else
            ? this
            : KeepSpelling(new IrIf(condition, then, @else, Source, Uniformity));

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrIf WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity ? this : KeepSpelling(new IrIf(Condition, Then, Else, Source, uniformity));
}
