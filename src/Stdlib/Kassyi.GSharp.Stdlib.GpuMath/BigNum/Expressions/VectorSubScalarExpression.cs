namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class VectorSubScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.Add(vector.BitsPerElement, IBitBoundedExpression.Of(scalar)))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateScalarOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}
