namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A null-coalescing expression <c>a ?? b</c>.</summary>
/// <param name="value">The value, evaluated once.</param>
/// <param name="whenNull">The value when the first one is <see langword="null"/>.</param>
/// <param name="valueConversion">The conversion of a non-null first value to the result type.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrCoalesce(IrExpression value, IrExpression whenNull, IrConversionKind valueConversion,
    ITypeSymbol type, IrCategory category, IrSource source) : IrExpression(type, category, default, source)
{
    /// <summary>The value, evaluated once.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>The value when the first one is <see langword="null"/>.</summary>
    public IrExpression WhenNull { get; } = whenNull;

    /// <summary>The conversion of a non-null first value to the result type.</summary>
    public IrConversionKind ValueConversion { get; } = valueConversion;
}
