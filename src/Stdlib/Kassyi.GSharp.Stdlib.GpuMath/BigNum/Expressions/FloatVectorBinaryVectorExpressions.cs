namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly record struct FloatVectorZipOperands(
    GpuBigFloatArray Left,
    GpuBigFloatArray Right,
    GpuBigFloatArray Destination);

internal abstract class FloatVectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    int wordsPerElement) : IGpuBigFloatVectorExpression
{
    protected const int DefaultDivShiftBits = 32;

    public int Length => left.Length;
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
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

        var evalCount = elementCount ?? (Length - elementOffset);
        var leftResult = left.Evaluate(workspace, elementOffset, evalCount);
        var rightResult = right.Evaluate(workspace, elementOffset, evalCount);

        if (leftResult.Length != rightResult.Length)
        {
            throw new ArgumentException("Vector lengths must match for Zip operations.");
        }

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

        var operands = new FloatVectorZipOperands(leftResult, rightResult, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected static TContext CreateZipContext<TContext>(FloatVectorZipOperands operands)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorFloatBinaryAotContext
    {
        var ctx = default(TContext);
        ctx.InitializeFloatBinary(
            operands.Left.Significands.DevicePointer,
            operands.Right.Significands.DevicePointer,
            operands.Destination.Significands.DevicePointer,
            operands.Left.Exponents.DevicePointer,
            operands.Right.Exponents.DevicePointer,
            operands.Destination.Exponents.DevicePointer,
            operands.Left.Signs.DevicePointer,
            operands.Right.Signs.DevicePointer,
            operands.Destination.Signs.DevicePointer,
            operands.Left.WordsPerElement,
            operands.Right.WordsPerElement,
            operands.Destination.WordsPerElement,
            scalarExp: 0,
            scalarSign: 0u,
            isScalar: false);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX);

    public void Dispose()
    {
        if (_allocated)
        {
            _result.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal sealed class FloatVectorAddVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(operands);
        ctx.OpSign = 0u;
        GpuBigIntVectorKernels.LaunchVectorFloatZipAddKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSubVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(operands);
        ctx.OpSign = 1u;
        GpuBigIntVectorKernels.LaunchVectorFloatZipSubKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorMulVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        left.WordsPerElement + right.WordsPerElement)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatMul>(operands);
        GpuBigIntVectorKernels.LaunchVectorFloatZipMulKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatDiv>(operands);
        ctx.PreShiftBits = DefaultDivShiftBits;
        GpuBigIntVectorKernels.LaunchVectorFloatZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorPowVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right)
    : FloatVectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorZipOperands operands,
        uint gridDimX) =>
        FloatVectorElementwiseHelper.ExecuteBinary(
            workspace,
            operands.Left,
            operands.Right,
            operands.Destination,
            (int)gridDimX,
            static (ref ctx, arith, in l, in r, dst) =>
                arith.Pow(ref ctx, in l, in r, dst));
}
