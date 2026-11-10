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
}
