namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class VectorMulScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.Multiply(vector.BitsPerElement, IBitBoundedExpression.Of(scalar)))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorMul
        {
            Op = CreateScalarOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}
