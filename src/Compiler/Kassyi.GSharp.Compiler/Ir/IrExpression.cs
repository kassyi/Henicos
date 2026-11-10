namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>An expression: a node with a type, a value category and possibly a constant value.</summary>
/// <param name="type">The type of the value.</param>
/// <param name="category">How the node that holds the expression uses it.</param>
/// <param name="constantValue">The C# constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrExpression(ITypeSymbol type, IrCategory category, Optional<object?> constantValue,
    IrSource source) : IrNode(source)
{
    /// <summary>The type of the value; conversions are explicit <see cref="Expressions.IrConversion"/> nodes.</summary>
    public ITypeSymbol Type { get; } = type;

    /// <summary>How the node that holds the expression uses it.</summary>
    public IrCategory Category { get; } = category;

    /// <summary>
    /// The C# constant value. The builder keeps the structure of a constant expression (<c>1 + 2</c> stays a binary
    /// node); whether to fold it is decided later.
    /// </summary>
    public Optional<object?> ConstantValue { get; } = constantValue;
}
