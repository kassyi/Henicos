namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// A constant: a literal, a read of a <see langword="const"/> field or local, a constant <see langword="default"/>,
/// <see langword="sizeof"/> or <see langword="nameof"/>, or the value of an omitted optional parameter.
/// </summary>
/// <remarks>A constant is a value; a <see langword="null"/> pointer is a <see cref="IrCategory.Pointer"/>.</remarks>
/// <param name="value">The value.</param>
/// <param name="origin">The <see langword="const"/> field or local that was read; <see langword="null"/>
/// otherwise.</param>
/// <param name="type">The type of the constant.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="literalText">
/// The literal as written (<c>0x10</c>, <c>1_000</c>, <c>2f</c>, <c>'a'</c>, <c>"s"</c>, <c>null</c>, <c>true</c>)
/// when the constant is a literal of the source; null otherwise. Spelling only.
/// </param>
internal sealed class IrConstant(object? value, ISymbol? origin, ITypeSymbol type, IrSource source,
    string? literalText = null)
    : IrExpression(type, type is IPointerTypeSymbol ? IrCategory.Pointer : IrCategory.Value,
        new Optional<object?>(value), source)
{
    /// <summary>The value.</summary>
    public object? Value { get; } = value;

    /// <summary>The <see langword="const"/> field or local that was read; <see langword="null"/> otherwise.</summary>
    public ISymbol? Origin { get; } = origin;

    /// <summary>
    /// The literal as written (<c>0x10</c>, <c>1_000</c>, <c>2f</c>, <c>'a'</c>, <c>"s"</c>, <c>null</c>, <c>true</c>)
    /// when the constant is a literal of the source; null otherwise. Spelling only.
    /// </summary>
    public string? LiteralText { get; } = literalText;
}
