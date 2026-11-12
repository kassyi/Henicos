namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorDivVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override bool ChecksZeroDivisor => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateZipFloatOp(operands),
            ZeroDivisor = operands.ZeroDivisor.ToProxy()
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.FloatOp.Core, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorFloatZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
