namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorPowVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement))
{
    protected override bool RunsOnHost => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX) =>
        FloatVectorElementwiseHelper.ExecuteBinary(
            workspace,
            operands.Left,
            operands.Right,
            operands.Destination,
            (int)gridDimX,
            static (ref ctx, arith, in l, in r, dst) =>
                arith.Pow(ref ctx, in l, in r, dst));
}
