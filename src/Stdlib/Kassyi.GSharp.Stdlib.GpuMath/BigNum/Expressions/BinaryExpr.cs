namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A zero-allocation generic binary expression node representing a deferred arbitrary-precision floating-point operation.
/// </summary>
/// <typeparam name="T">The operand value type.</typeparam>
/// <typeparam name="TOp">The operator type implementing static abstract polymorphism.</typeparam>
/// <param name="left">The left operand.</param>
/// <param name="right">The right operand.</param>
/// <remarks>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly struct wrapping value-type operands and a static operator tag, evaluation requires no heap allocations.
/// </para>
/// </remarks>
public readonly struct BinaryExpr<T, TOp>(T left, T right)
    where TOp : struct, IFloatOperator<T>
{
    /// <summary>
    /// The left operand.
    /// </summary>
    public readonly T Left = left;

    /// <summary>
    /// The right operand.
    /// </summary>
    public readonly T Right = right;

    /// <summary>
    /// Evaluates this binary expression using the provided expression visitor.
    /// </summary>
    /// <param name="visitor">The expression visitor instance.</param>
    /// <returns>The evaluated floating-point result.</returns>
    public T Evaluate(ref GpuBigFloatExpressionVisitor visitor) => TOp.Execute(Left, Right, ref visitor);
}
