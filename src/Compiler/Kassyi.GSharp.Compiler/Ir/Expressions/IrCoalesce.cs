using Kassyi.GSharp.Compiler.Ir.Decisions;

namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A null-coalescing expression <c>a ?? b</c>.</summary>
/// <param name="value">The value, evaluated once.</param>
/// <param name="whenNull">The value when the first one is <see langword="null"/>.</param>
/// <param name="valueConversion">The conversion of a non-null first value to the result type.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">
/// Whether the condition is the same in every thread of a warp; unclassified when built.
/// </param>
internal sealed class IrCoalesce(IrExpression value, IrExpression whenNull, IrConversionKind valueConversion,
    ITypeSymbol type, IrCategory category, IrSource source, IrUniformity uniformity = IrUniformity.Unclassified)
    : IrExpression(type, category, default, source)
{
    /// <summary>The value, evaluated once.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>The value when the first one is <see langword="null"/>.</summary>
    public IrExpression WhenNull { get; } = whenNull;

    /// <summary>The conversion of a non-null first value to the result type.</summary>
    public IrConversionKind ValueConversion { get; } = valueConversion;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrCoalesce Update(IrExpression value, IrExpression whenNull) =>
        value == Value && whenNull == WhenNull
            ? this
            : KeepSpelling(new IrCoalesce(value, whenNull, ValueConversion, Type, Category, Source, Uniformity));

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrCoalesce WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity
            ? this
            : KeepSpelling(new IrCoalesce(Value, WhenNull, ValueConversion, Type, Category, Source, uniformity));

    /// <summary>The decisions of the pass; an error before it ran.</summary>
    public IrCoalesceDecision Decided => GetDecision<IrCoalesceDecision>();

    /// <summary>This node with <paramref name="decision"/>.</summary>
    public IrCoalesce WithDecision(IrCoalesceDecision decision) =>
        IrExpression.WithDecision<IrCoalesce>(this, decision);
}
