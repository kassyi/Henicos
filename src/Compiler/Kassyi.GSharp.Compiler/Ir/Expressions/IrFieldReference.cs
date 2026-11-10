namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A field of a receiver, or a static field.</summary>
/// <param name="receiver">The receiver; <see langword="null"/> for a static field.</param>
/// <param name="field">The field.</param>
/// <param name="category">How the holder uses the field.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrFieldReference(IrExpression? receiver, IFieldSymbol field, IrCategory category,
    Optional<object?> constantValue, IrSource source)
    : IrExpression(field.Type, category, constantValue, source)
{
    /// <summary>The receiver; <see langword="null"/> for a static field.</summary>
    public IrExpression? Receiver { get; } = receiver;

    /// <summary>The field.</summary>
    public IFieldSymbol Field { get; } = field;
}
