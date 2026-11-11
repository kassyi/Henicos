namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// A property or an indexer of a receiver, or a static property. Whether it becomes a field, a getter call or an
/// intrinsic is decided after the build.
/// </summary>
/// <param name="receiver">The receiver; <see langword="null"/> for a static property.</param>
/// <param name="property">The property or indexer.</param>
/// <param name="arguments">The indexer arguments in parameter order; empty for a property.</param>
/// <param name="category">How the holder uses the property.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrPropertyReference(IrExpression? receiver, IPropertySymbol property,
    ImmutableArray<IrArgument> arguments, IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(property.Type, category, constantValue, source)
{
    /// <summary>The receiver; <see langword="null"/> for a static property.</summary>
    public IrExpression? Receiver { get; } = receiver;

    /// <summary>The property or indexer.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The indexer arguments in parameter order; empty for a property.</summary>
    public ImmutableArray<IrArgument> Arguments { get; } = arguments;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrPropertyReference Update(IrExpression? receiver, ImmutableArray<IrArgument> arguments) =>
        receiver == Receiver && arguments == Arguments
            ? this
            : new IrPropertyReference(receiver, Property, arguments, Category, ConstantValue, Source);
}
