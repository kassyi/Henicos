namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// The creation of a one-dimensional array, <c>new T[n]</c> or <c>new T[] { ... }</c>, which device code keeps on the
/// stack.
/// </summary>
/// <param name="elementType">The element type.</param>
/// <param name="length">The length.</param>
/// <param name="elements">The initializer elements; empty without an initializer.</param>
/// <param name="type">The array type.</param>
/// <param name="category">How the holder uses the array.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrArrayCreation(ITypeSymbol elementType, IrExpression length,
    ImmutableArray<IrExpression> elements, ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, default, source)
{
    /// <summary>The element type.</summary>
    public ITypeSymbol ElementType { get; } = elementType;

    /// <summary>The length.</summary>
    public IrExpression Length { get; } = length;

    /// <summary>The initializer elements; empty without an initializer.</summary>
    public ImmutableArray<IrExpression> Elements { get; } = elements;
}
