namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an arithmetic operator capable of executing operations on arbitrary-precision float operands via static abstract polymorphism.
/// </summary>
/// <typeparam name="T">The floating-point operand type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Employs C# generic math static abstract methods to achieve zero-overhead operator abstraction
/// without runtime delegate allocation or virtual dispatch overhead.
/// </para>
/// </remarks>
public interface IFloatOperator<T>
{
    /// <summary>
    /// Executes the specified arithmetic operator using the provided float expression visitor.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="visitor">The expression visitor responsible for evaluating intermediate nodes.</param>
    /// <returns>The evaluated floating-point result.</returns>
    static abstract T Execute(T left, T right, ref GpuBigFloatExpressionVisitor visitor);
}

/// <summary>
/// Static addition operator tag for arbitrary-precision floating-point operands.
/// </summary>
public readonly struct AddOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    /// <inheritdoc/>
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitAdd(left, right);

    /// <inheritdoc/>
    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right,
        ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitAdd(left, right);
}

/// <summary>
/// Static subtraction operator tag for arbitrary-precision floating-point operands.
/// </summary>
public readonly struct SubOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    /// <inheritdoc/>
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitSub(left, right);

    /// <inheritdoc/>
    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right,
        ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitSub(left, right);
}

/// <summary>
/// Static multiplication operator tag for arbitrary-precision floating-point operands.
/// </summary>
[ExcludeFromCodeCoverage]
public readonly struct MulOp : IFloatOperator<GpuBigFloat>
{
    /// <inheritdoc/>
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitMul(left, right);
}

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

