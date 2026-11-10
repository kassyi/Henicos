namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// The creation of a struct value, <c>new S(args) { F = v }</c>; the member assignments target the value being built
/// (<see cref="IrInitializedObject"/>).
/// </summary>
/// <param name="constructor">The constructor (an implicit parameterless one included).</param>
/// <param name="arguments">The constructor arguments in parameter order.</param>
/// <param name="initializers">The member assignments of the object initializer, in the order written.</param>
/// <param name="type">The struct type.</param>
/// <param name="category">How the holder uses the value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrStructCreation(IMethodSymbol? constructor, ImmutableArray<IrArgument> arguments,
    ImmutableArray<IrAssignment> initializers, ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, default, source)
{
    /// <summary>The constructor (an implicit parameterless one included).</summary>
    public IMethodSymbol? Constructor { get; } = constructor;

    /// <summary>The constructor arguments in parameter order.</summary>
    public ImmutableArray<IrArgument> Arguments { get; } = arguments;

    /// <summary>The member assignments of the object initializer, in the order written.</summary>
    public ImmutableArray<IrAssignment> Initializers { get; } = initializers;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrStructCreation Update(ImmutableArray<IrArgument> arguments,
        ImmutableArray<IrAssignment> initializers) =>
        arguments == Arguments && initializers == Initializers
            ? this
            : new IrStructCreation(Constructor, arguments, initializers, Type, Category, Source);
}
