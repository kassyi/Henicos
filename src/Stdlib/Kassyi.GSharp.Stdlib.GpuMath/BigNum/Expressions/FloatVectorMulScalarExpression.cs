namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorMulScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        vector.WordsPerElement + scalar.Significand.NumWords)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatMul
        {
            FloatOp = CreateScalarFloatOp(operands)
        };
        GpuBigIntVectorKernels.LaunchVectorFloatMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}
