namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>An element of a one-dimensional array.</summary>
/// <param name="receiver">The array.</param>
/// <param name="index">The index.</param>
/// <param name="type">The element type.</param>
/// <param name="category">How the holder uses the element.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrElementReference(IrExpression receiver, IrExpression index, ITypeSymbol type,
    IrCategory category, IrSource source) : IrExpression(type, category, default, source)
{
    /// <summary>The array.</summary>
    public IrExpression Receiver { get; } = receiver;

    /// <summary>The index.</summary>
    public IrExpression Index { get; } = index;
}
