namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class VectorModScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar,
        BigIntWidths.OfWords(Math.Max(vector.WordsPerElement, scalar.NumWords)))
{
    protected override bool ElementMayThrow => true;

    protected override bool DividesByZero(GpuWorkspace workspace, VectorScalarOperands operands)
    {
        ZeroDivisorCheck.ThrowIfCapturingScalarTest(workspace);
        return operands.Scalar.IsZero();
    }

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorDivMod
        {
            Op = CreateScalarOp(operands),
            IsModulo = true
        };
        using var scratch = DivModScratch.Rent(workspace, ctx.Op, gridDimX);
        ctx.Scratch = scratch?.ToProxy() ?? default;
        GpuBigIntVectorKernels.LaunchVectorModScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
