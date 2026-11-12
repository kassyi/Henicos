namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorPowScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand exponentScalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        exponentScalar,
        Math.Max(vector.WordsPerElement, exponentScalar.Significand.NumWords))
{
    protected override bool RunsOnHost => true;

    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX)
    {
        var expOperand = operands.Scalar;
        FloatVectorElementwiseHelper.ExecuteUnary(
            workspace,
            operands.Vector,
            operands.Destination,
            (int)gridDimX,
            (ref GpuBigIntExecutionContext ctx, IGpuBigFloatArithmetic arith, in GpuFloatOperand src, GpuBigIntMemoryHandle dst) =>
                arith.Pow(ref ctx, in src, in expOperand, dst));
    }
}
