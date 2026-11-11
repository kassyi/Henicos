namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// A comparison of a value with <see langword="null"/> by the built-in operator (<c>x == null</c>, <c>x != null</c>,
/// <c>x is null</c>, <c>x is not null</c>); how <see langword="null"/> is represented is decided after the build.
/// </summary>
/// <param name="operand">The value compared with <see langword="null"/>.</param>
/// <param name="isNegated">Whether the test is <c>!=</c> or <c>is not null</c>.</param>
/// <param name="type">The result type (<see langword="bool"/>).</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrNullTest(IrExpression operand, bool isNegated, ITypeSymbol type, IrCategory category,
    IrSource source) : IrExpression(type, category, default, source)
{
    /// <summary>The value compared with <see langword="null"/>.</summary>
    public IrExpression Operand { get; } = operand;

    /// <summary>Whether the test is <c>!=</c> or <c>is not null</c>.</summary>
    public bool IsNegated { get; } = isNegated;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrNullTest Update(IrExpression operand) =>
        operand == Operand ? this : new IrNullTest(operand, IsNegated, Type, Category, Source);
}
