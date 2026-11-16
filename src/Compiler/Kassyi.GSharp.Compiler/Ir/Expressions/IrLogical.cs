namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A short-circuit <c>&amp;&amp;</c> or <c>||</c> of two <see langword="bool"/> operands.</summary>
/// <param name="isAnd">Whether the operator is <c>&amp;&amp;</c> (otherwise <c>||</c>).</param>
/// <param name="left">The left operand, always evaluated.</param>
/// <param name="right">The right operand, evaluated only when the left one does not decide the result.</param>
/// <param name="type">The result type.</param>
/// <param name="category">How the holder uses the result.</param>
/// <param name="constantValue">The constant value, as Roslyn gives it.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">
/// Whether the condition is the same in every thread of a warp; unclassified when built.
/// </param>
internal sealed class IrLogical(bool isAnd, IrExpression left, IrExpression right, ITypeSymbol type,
    IrCategory category, Optional<object?> constantValue, IrSource source,
    IrUniformity uniformity = IrUniformity.Unclassified)
    : IrExpression(type, category, constantValue, source)
{
    /// <summary>Whether the operator is <c>&amp;&amp;</c> (otherwise <c>||</c>).</summary>
    public bool IsAnd { get; } = isAnd;

    /// <summary>The left operand, always evaluated.</summary>
    public IrExpression Left { get; } = left;

    /// <summary>The right operand, evaluated only when the left one does not decide the result.</summary>
    public IrExpression Right { get; } = right;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrLogical Update(IrExpression left, IrExpression right) =>
        left == Left && right == Right
            ? this
            : KeepSpelling(new IrLogical(IsAnd, left, right, Type, Category, ConstantValue, Source, Uniformity));

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrLogical WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity
            ? this
            : KeepSpelling(new IrLogical(IsAnd, Left, Right, Type, Category, ConstantValue, Source, uniformity));
}
