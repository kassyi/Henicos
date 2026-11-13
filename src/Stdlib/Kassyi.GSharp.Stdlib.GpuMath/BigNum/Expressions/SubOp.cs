namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
