namespace Kassyi.GSharp.Stdlib.GpuMath.GpuBigFloat.Expressions;

/// <summary>
/// Defines an arithmetic operator capable of executing operations on float operands via static polymorphism.
/// </summary>
public interface IFloatOperator<T>
{
    static abstract T Execute(T left, T right, ref GpuBigFloatExpressionVisitor visitor);
}

/// <summary>
/// Addition operator for floating-point operands.
/// </summary>
public readonly struct AddOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitAdd(left, right);

    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right,
        ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitAdd(left, right);
}

/// <summary>
/// Subtraction operator for floating-point operands.
/// </summary>
public readonly struct SubOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitSub(left, right);

    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right,
        ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitSub(left, right);
}

/// <summary>
/// Multiplication operator for floating-point operands.
/// </summary>
public readonly struct MulOp : IFloatOperator<GpuBigFloat>
{
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitMul(left, right);
}

/// <summary>
/// A zero-allocation generic binary expression node representing a deferred operation.
/// </summary>
/// <typeparam name="T">The operand value type.</typeparam>
/// <typeparam name="TOp">The operator type implementing static polymorphism.</typeparam>
public readonly struct BinaryExpr<T, TOp>(T left, T right)
    where TOp : struct, IFloatOperator<T>
{
    public readonly T Left = left;
    public readonly T Right = right;

    public T Evaluate(ref GpuBigFloatExpressionVisitor visitor) => TOp.Execute(Left, Right, ref visitor);
}
