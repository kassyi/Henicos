namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU arbitrary-precision floating-point numbers.
/// </summary>
public interface IGpuBigFloatVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit significand words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Evaluates the vectorized floating-point expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">An optional destination array for the evaluated values. If provided, it must have a length of elementCount.</param>
    /// <returns>A <see cref="GpuBigFloatArray"/> containing the evaluated vector slice results.</returns>
    GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null);
}

internal sealed class GpuBigFloatVectorLeafExpression(GpuBigFloatArray array) : IGpuBigFloatVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        var slice = (elementOffset == 0 && count == Length)
            ? array
            : array.Slice(elementOffset, count);

        if (targetResult != null)
        {
            var dest = targetResult.Value;
            for (var i = 0; i < count; i++)
            {
                dest.SetElement(i, slice.GetElement(i));
            }

            return dest;
        }

        return slice;
    }

    public void Dispose()
    {
        // Leaf expression does not own the source array.
    }
}

internal abstract class FloatVectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar,
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

        DispatchKernel(workspace, vecResult, scalar, destination, (uint)evalCount);
        return destination;
    }

    protected static TContext CreateScalarContext<TContext>(
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorFloatBinaryAotContext
    {
        var scalarSig = scalarValue.UFloat.Significand;
        var ctx = default(TContext);
        ctx.InitializeFloatBinary(
            vecResult.Significands.DevicePointer,
            scalarSig.DevicePointer,
            destination.Significands.DevicePointer,
            vecResult.Exponents.DevicePointer,
            vecResult.Exponents.DevicePointer,
            destination.Exponents.DevicePointer,
            vecResult.Signs.DevicePointer,
            vecResult.Signs.DevicePointer,
            destination.Signs.DevicePointer,
            vecResult.WordsPerElement,
            scalarSig.NumWords,
            destination.WordsPerElement,
            scalarValue.UFloat.Exponent,
            scalarValue.IsNegative ? 1u : 0u,
            isScalar: true);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination,
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
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(
            vecResult, scalarValue, destination);
        ctx.OpSign = 0u;
        GpuBigIntVectorKernels.LaunchVectorFloatAddScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSubScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(
            vecResult, scalarValue, destination);
        ctx.OpSign = 1u;
        GpuBigIntVectorKernels.LaunchVectorFloatSubScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorMulScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        vector.WordsPerElement + scalar.UFloat.Significand.NumWords)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatMul>(
            vecResult, scalarValue, destination);
        GpuBigIntVectorKernels.LaunchVectorFloatMulScalarKernelPtr(
            ctx, gridDimX, 1, 1, BlockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorDivScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
{
    protected override void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray vecResult,
        GpuBigFloat scalarValue,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorFloatDiv>(
            vecResult, scalarValue, destination);
        ctx.PreShiftBits = DefaultDivShiftBits;
        GpuBigIntVectorKernels.LaunchVectorFloatDivScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

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

        DispatchKernel(workspace, leftResult, rightResult, destination, (uint)evalCount);
        return destination;
    }

    protected static TContext CreateZipContext<TContext>(
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorFloatBinaryAotContext
    {
        var ctx = default(TContext);
        ctx.InitializeFloatBinary(
            leftResult.Significands.DevicePointer,
            rightResult.Significands.DevicePointer,
            destination.Significands.DevicePointer,
            leftResult.Exponents.DevicePointer,
            rightResult.Exponents.DevicePointer,
            destination.Exponents.DevicePointer,
            leftResult.Signs.DevicePointer,
            rightResult.Signs.DevicePointer,
            destination.Signs.DevicePointer,
            leftResult.WordsPerElement,
            rightResult.WordsPerElement,
            destination.WordsPerElement,
            scalarExp: 0,
            scalarSign: 0u,
            isScalar: false);
        return ctx;
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination,
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
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(
            leftResult, rightResult, destination);
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
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatAddSub>(
            leftResult, rightResult, destination);
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
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatMul>(
            leftResult, rightResult, destination);
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
        GpuBigFloatArray leftResult,
        GpuBigFloatArray rightResult,
        GpuBigFloatArray destination,
        uint gridDimX)
    {
        var ctx = CreateZipContext<GpuBigIntVectorKernels.AotContexts.VectorFloatDiv>(
            leftResult, rightResult, destination);
        ctx.PreShiftBits = DefaultDivShiftBits;
        GpuBigIntVectorKernels.LaunchVectorFloatZipDivKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class FloatVectorSkipExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int skipCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveSkip = Math.Clamp(skipCount, 0, inner.Length);

    public int Length => inner.Length - _effectiveSkip;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        return inner.Evaluate(workspace, _effectiveSkip + elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression
{
    private readonly int _effectiveTake = Math.Clamp(takeCount, 0, inner.Length);

    public int Length => _effectiveTake;
    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        return inner.Evaluate(workspace, elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}
