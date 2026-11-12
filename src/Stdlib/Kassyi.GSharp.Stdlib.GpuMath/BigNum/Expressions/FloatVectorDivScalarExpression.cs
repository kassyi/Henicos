namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorDivScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.Significand.NumWords) + 1)
{
    protected override bool ElementMayThrow => true;

    protected override bool DividesByZero(GpuWorkspace workspace, FloatVectorScalarOperands operands)
    {
        ZeroDivisorCheck.ThrowIfCapturingScalarTest(workspace);
        return new GpuBigUIntView(workspace, operands.Scalar.Significand).IsZero();
    }

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatDiv
        {
            FloatOp = CreateScalarFloatOp(operands)
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.FloatOp.Core, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorFloatDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
