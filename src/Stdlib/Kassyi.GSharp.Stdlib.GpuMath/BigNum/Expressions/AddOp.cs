namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
