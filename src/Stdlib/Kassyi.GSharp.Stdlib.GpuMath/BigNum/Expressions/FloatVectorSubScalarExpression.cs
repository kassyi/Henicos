namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorSubScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub
        {
            FloatOp = CreateScalarFloatOp(operands),
            OpSign = 1u
        };
        GpuBigIntVectorKernels.LaunchVectorFloatSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}
