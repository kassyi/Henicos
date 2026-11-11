namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>A <see langword="return"/>, with or without a value.</summary>
/// <param name="value">The value, or for a method that returns by reference the location; <see langword="null"/> in a
/// <see langword="void"/> method.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrReturn(IrExpression? value, IrSource source) : IrStatement(source)
{
    /// <summary>The value, or the location returned by reference; <see langword="null"/> in a void method.</summary>
    public IrExpression? Value { get; } = value;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrReturn Update(IrExpression? value) =>
        value == Value ? this : new IrReturn(value, Source);
}
