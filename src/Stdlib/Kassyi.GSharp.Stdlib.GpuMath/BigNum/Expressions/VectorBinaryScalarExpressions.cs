namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly record struct VectorScalarOperands(
    GpuBigIntArray Vector,
    GpuBigInt Scalar,
    GpuBigIntArray Destination);

internal abstract class VectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    int wordsPerElement) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
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
        var evalCount = elementCount ?? (Length - elementOffset);
        var vecResult = vector.Evaluate(workspace, elementOffset, evalCount);
        var scalarResult = scalar.Evaluate(workspace);

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
        var operands = new VectorScalarOperands(vecResult, scalarResult, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected TContext CreateScalarContext<TContext>(VectorScalarOperands operands)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorBinaryAotContext
    {
        var ctx = default(TContext);
        ctx.InitializeBinary(
            operands.Vector.Values.DevicePointer,
            operands.Scalar.Value.DevicePointer,
            operands.Destination.Values.DevicePointer,
            operands.Vector.Signs.DevicePointer,
            operands.Vector.Signs.DevicePointer,
            operands.Destination.Signs.DevicePointer,
            vector.WordsPerElement,
            operands.Scalar.Value.NumWords,
            WordsPerElement,
            operands.Scalar.IsNegative ? 1u : 0u,
            isScalar: true);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX);

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _finalSigns.Dispose();
            _allocated = false;
        }

        vector.Dispose();
    }
}

internal sealed class VectorMulScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, vector.WordsPerElement + scalar.NumWords)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorMul>(operands);
        GpuBigIntVectorKernels.LaunchVectorMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorAddScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorAddSub>(operands);
        ctx.OpSign = 0u;
        GpuBigIntVectorKernels.LaunchVectorAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorSubScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorAddSub>(operands);
        ctx.OpSign = 1u;
        GpuBigIntVectorKernels.LaunchVectorSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorDivScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        if (operands.Scalar.IsZero())
        {
            throw new DivideByZeroException();
        }

        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorDivMod>(operands);
        ctx.IsModulo = false;
        GpuBigIntVectorKernels.LaunchVectorDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorModScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar)
    : VectorBinaryScalarExpressionBase(vector, scalar, Math.Max(vector.WordsPerElement, scalar.NumWords))
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX)
    {
        if (operands.Scalar.IsZero())
        {
            throw new DivideByZeroException();
        }

        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorDivMod>(operands);
        ctx.IsModulo = true;
        GpuBigIntVectorKernels.LaunchVectorModScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}
