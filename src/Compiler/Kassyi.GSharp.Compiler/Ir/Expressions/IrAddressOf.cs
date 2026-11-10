namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>The address of a location, <c>&amp;x</c>.</summary>
/// <param name="operand">The location (<see cref="IrCategory.Location"/>).</param>
/// <param name="type">The pointer type.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrAddressOf(IrExpression operand, ITypeSymbol type, IrSource source)
    : IrExpression(type, IrCategory.Pointer, default, source)
{
    /// <summary>The location.</summary>
    public IrExpression Operand { get; } = operand;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrAddressOf Update(IrExpression operand) =>
        operand == Operand ? this : new IrAddressOf(operand, Type, Source);
}
