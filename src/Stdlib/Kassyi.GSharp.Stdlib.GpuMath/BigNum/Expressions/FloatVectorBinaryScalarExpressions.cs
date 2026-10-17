namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly record struct FloatVectorScalarOperands(
    GpuBigFloatArray Vector,
    GpuFloatOperand Scalar,
    GpuBigFloatArray Destination);

internal abstract class FloatVectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    int wordsPerElement) : IGpuBigFloatVectorExpression
{
    protected const int DefaultDivShiftBits = 32;

    public int Length => vector.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    private GpuBigFloatArray _result;
    private bool _allocated;

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);

        GpuBigFloatArray destination;
        if (targetResult != null)
        {
            destination = targetResult.Value;
        }
        else
        {
            if (!_allocated)
            {
                _result = new GpuBigFloatArray(workspace, evalCount, WordsPerElement);
                _allocated = true;
            }

            destination = _result;
        }

        var operands = new FloatVectorScalarOperands(vecResult, scalar, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected static TContext CreateScalarContext<TContext>(FloatVectorScalarOperands operands)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorFloatBinaryAotContext
    {
        var scalarSig = operands.Scalar.Significand;
        var ctx = default(TContext);
        ctx.InitializeFloatBinary(
            operands.Vector.Significands.DevicePointer,
            scalarSig.DevicePointer,
            operands.Destination.Significands.DevicePointer,
            operands.Vector.Exponents.DevicePointer,
            operands.Vector.Exponents.DevicePointer,
            operands.Destination.Exponents.DevicePointer,
            operands.Vector.Signs.DevicePointer,
            operands.Vector.Signs.DevicePointer,
            operands.Destination.Signs.DevicePointer,
            operands.Vector.WordsPerElement,
            scalarSig.NumWords,
            operands.Destination.WordsPerElement,
            operands.Scalar.Exponent,
            operands.Scalar.IsNegative ? 1u : 0u,
            isScalar: true);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
        uint gridDimX);

    public void Dispose()
    {
        if (_allocated)
        {
            _result.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal sealed class FloatVectorAddScalarExpression(
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
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(operands);
        ctx.OpSign = 0u;
        GpuBigIntVectorKernels.LaunchVectorFloatAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

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
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(operands);
        ctx.OpSign = 1u;
        GpuBigIntVectorKernels.LaunchVectorFloatSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

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
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatMul>(operands);
        GpuBigIntVectorKernels.LaunchVectorFloatMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivScalarExpression(
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
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatDiv>(operands);
        ctx.PreShiftBits = DefaultDivShiftBits;
        GpuBigIntVectorKernels.LaunchVectorFloatDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorPowScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand exponentScalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        exponentScalar,
        Math.Max(vector.WordsPerElement, exponentScalar.Significand.NumWords))
{
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
