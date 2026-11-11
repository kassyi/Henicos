using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A unary operator (<c>-x</c>, <c>~x</c>, <c>!x</c>, <c>+x</c>).</summary>
/// <param name="operator">The operator.</param>
/// <param name="operand">The operand.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrUnary(IrOperator<UnaryOperatorKind> @operator, IrExpression operand, ITypeSymbol type,
    IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>The operator.</summary>
    public IrOperator<UnaryOperatorKind> Operator { get; } = @operator;

    /// <summary>The operand.</summary>
    public IrExpression Operand { get; } = operand;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrUnary Update(IrExpression operand) =>
        operand == Operand ? this : new IrUnary(Operator, operand, Type, Category, ConstantValue, Source);
}
