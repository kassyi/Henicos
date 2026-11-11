using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// A binary operator other than <c>&amp;&amp;</c> and <c>||</c> (<see cref="IrLogical"/>) and a comparison with
/// <see langword="null"/> (<see cref="IrNullTest"/>).
/// </summary>
/// <param name="operator">The operator.</param>
/// <param name="left">The left operand.</param>
/// <param name="right">The right operand.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrBinary(IrOperator<BinaryOperatorKind> @operator, IrExpression left, IrExpression right,
    ITypeSymbol type, IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>The operator.</summary>
    public IrOperator<BinaryOperatorKind> Operator { get; } = @operator;

    /// <summary>The left operand.</summary>
    public IrExpression Left { get; } = left;

    /// <summary>The right operand.</summary>
    public IrExpression Right { get; } = right;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrBinary Update(IrExpression left, IrExpression right) =>
        left == Left && right == Right
            ? this
            : new IrBinary(Operator, left, right, Type, Category, ConstantValue, Source);
}
