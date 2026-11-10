namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A short-circuit <c>&amp;&amp;</c> or <c>||</c> of two <see langword="bool"/> operands.</summary>
/// <param name="isAnd">Whether the operator is <c>&amp;&amp;</c> (otherwise <c>||</c>).</param>
/// <param name="left">The left operand, always evaluated.</param>
/// <param name="right">The right operand, evaluated only when the left one does not decide the result.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrLogical(bool isAnd, IrExpression left, IrExpression right, ITypeSymbol type,
    IrCategory category, Optional<object?> constantValue, IrSource source)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>Whether the operator is <c>&amp;&amp;</c> (otherwise <c>||</c>).</summary>
    public bool IsAnd { get; } = isAnd;

    /// <summary>The left operand, always evaluated.</summary>
    public IrExpression Left { get; } = left;

    /// <summary>The right operand, evaluated only when the left one does not decide the result.</summary>
    public IrExpression Right { get; } = right;
}
