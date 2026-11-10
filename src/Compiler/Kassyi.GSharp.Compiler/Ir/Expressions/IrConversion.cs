namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A conversion, explicit (a cast) or implicit.</summary>
/// <param name="operand">The converted value.</param>
/// <param name="conversion">The kind of conversion, the user-defined operator if any, and the overflow context.</param>
/// <param name="isImplicit">Whether C# inserted the conversion (no cast is written).</param>
/// <param name="type">The type converted to.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrConversion(IrExpression operand, IrOperator<IrConversionKind> conversion, bool isImplicit,
    ITypeSymbol type, IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>The converted value.</summary>
    public IrExpression Operand { get; } = operand;

    /// <summary>The kind of conversion, the user-defined operator if any, and the overflow context.</summary>
    public IrOperator<IrConversionKind> Conversion { get; } = conversion;

    /// <summary>Whether C# inserted the conversion (no cast is written).</summary>
    public bool IsImplicit { get; } = isImplicit;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrConversion Update(IrExpression operand) =>
        operand == Operand
            ? this
            : new IrConversion(operand, Conversion, IsImplicit, Type, Category, ConstantValue, Source);
}
