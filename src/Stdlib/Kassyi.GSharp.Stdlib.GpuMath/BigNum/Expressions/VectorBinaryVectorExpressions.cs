namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly record struct VectorZipOperands(
    GpuBigIntArray Left,
    GpuBigIntArray Right,
    GpuBigIntArray Destination);

internal abstract class VectorBinaryVectorExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    int wordsPerElement,
    bool checkConstructorLength = false) : IGpuBigIntVectorExpression
{
    public int Length => left.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public GpuBigIntArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        if (checkConstructorLength && left.Length != right.Length)
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

        if (targetResult != null)
        {
            _finalValues = targetResult.Value;
        }
        else if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, evalCount, WordsPerElement);
            _finalSigns = new GpuBigUIntArray(workspace, evalCount, 1);
            _allocated = true;
        }

        var destination = new GpuBigIntArray(_finalValues, _finalSigns);
        var operands = new VectorZipOperands(leftResult, rightResult, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected TContext CreateZipContext<TContext>(VectorZipOperands operands)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorBinaryAotContext
    {
        var ctx = default(TContext);
        ctx.InitializeBinary(
            operands.Left.Values.DevicePointer,
            operands.Right.Values.DevicePointer,
            operands.Destination.Values.DevicePointer,
            operands.Left.Signs.DevicePointer,
            operands.Right.Signs.DevicePointer,
            operands.Destination.Signs.DevicePointer,
            operands.Left.WordsPerElement,
            operands.Right.WordsPerElement,
            WordsPerElement,
            scalarSign: 0u,
            isScalar: false);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX);

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _finalSigns.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal sealed class VectorAddVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, Math.Max(left.WordsPerElement, right.WordsPerElement) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorAddSub>(operands);
        ctx.OpSign = 0u;
        GpuBigIntVectorKernels.LaunchVectorZipAddKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorSubVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, Math.Max(left.WordsPerElement, right.WordsPerElement))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorAddSub>(operands);
        ctx.OpSign = 1u;
        GpuBigIntVectorKernels.LaunchVectorZipSubKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorMulVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(left, right, left.WordsPerElement + right.WordsPerElement)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorMul>(operands);
        GpuBigIntVectorKernels.LaunchVectorZipMulKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorDivVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement),
        checkConstructorLength: true)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorDivMod>(operands);
        ctx.IsModulo = false;
        GpuBigIntVectorKernels.LaunchVectorZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorModVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right)
    : VectorBinaryVectorExpressionBase(
        left,
        right,
        Math.Max(left.WordsPerElement, right.WordsPerElement),
        checkConstructorLength: true)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorZipOperands operands,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorDivMod>(operands);
        ctx.IsModulo = true;
        GpuBigIntVectorKernels.LaunchVectorZipModKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
