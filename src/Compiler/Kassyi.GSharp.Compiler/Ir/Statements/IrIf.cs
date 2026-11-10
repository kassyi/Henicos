namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>An <see langword="if"/> statement.</summary>
/// <param name="condition">The condition.</param>
/// <param name="then">The statement when the condition holds.</param>
/// <param name="else">The statement when it does not, or <see langword="null"/>.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrIf(IrExpression condition, IrStatement then, IrStatement? @else, IrSource source)
    : IrStatement(source)
{
    /// <summary>The condition.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>The statement when the condition holds.</summary>
    public IrStatement Then { get; } = then;

    /// <summary>The statement when it does not, or <see langword="null"/>.</summary>
    public IrStatement? Else { get; } = @else;
}
