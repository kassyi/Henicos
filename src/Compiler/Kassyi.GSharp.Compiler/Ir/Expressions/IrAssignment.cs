namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A simple assignment <c>t = v</c>, or a ref assignment <c>t = ref v</c>.</summary>
/// <param name="target">The location assigned (<see cref="IrCategory.Location"/>).</param>
/// <param name="value">The value, or for a ref assignment the location referred to.</param>
/// <param name="isRef">Whether it is a ref assignment.</param>
/// <param name="type">The type of the assignment's value.</param>
/// <param name="category">How the holder uses the assignment's value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrAssignment(IrExpression target, IrExpression value, bool isRef, ITypeSymbol type,
    IrCategory category, IrSource source) : IrExpression(type, category, default, source)
{
    /// <summary>The location assigned.</summary>
    public IrExpression Target { get; } = target;

    /// <summary>The value, or for a ref assignment the location referred to.</summary>
    public IrExpression Value { get; } = value;

    /// <summary>Whether it is a ref assignment.</summary>
    public bool IsRef { get; } = isRef;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrAssignment Update(IrExpression target, IrExpression value) =>
        target == Target && value == Value ? this : new IrAssignment(target, value, IsRef, Type, Category, Source);
}
