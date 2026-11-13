namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Static division operator tag for arbitrary-precision floating-point operands.
/// </summary>
[ExcludeFromCodeCoverage]
public readonly struct DivOp : IFloatOperator<GpuBigFloat>, IFloatOperator<GpuBigUFloat>
{
    /// <inheritdoc/>
    public static GpuBigFloat Execute(GpuBigFloat left, GpuBigFloat right, ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitDiv(left, right);

    /// <inheritdoc/>
    public static GpuBigUFloat Execute(GpuBigUFloat left, GpuBigUFloat right,
        ref GpuBigFloatExpressionVisitor visitor) =>
        visitor.VisitDiv(left, right);
}
