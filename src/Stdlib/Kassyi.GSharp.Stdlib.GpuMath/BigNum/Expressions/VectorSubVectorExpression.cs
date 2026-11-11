namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class VectorSubVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, BigIntWidths.Add(left.BitsPerElement, right.BitsPerElement))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorAddSub
        {
            Op = CreateZipOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorZipSubKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}
