using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

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

internal readonly record struct FloatVectorScalarOperands(
    GpuBigFloatArray Vector,
    GpuBigFloat Scalar,
    GpuBigFloatArray Destination);

internal readonly record struct FloatVectorZipOperands(
    GpuBigFloatArray Left,
    GpuBigFloatArray Right,
    GpuBigFloatArray Destination);

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

        var operands = new FloatVectorScalarOperands(vecResult, scalar, destination);
        DispatchKernel(workspace, operands, (uint)evalCount);
        return destination;
    }

    protected static TContext CreateScalarContext<TContext>(FloatVectorScalarOperands operands)
        where TContext : struct, GpuBigIntVectorKernels.AotContexts.IVectorFloatBinaryAotContext
    {
        var scalarSig = operands.Scalar.UFloat.Significand;
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
            operands.Scalar.UFloat.Exponent,
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
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
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
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
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
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        vector.WordsPerElement + scalar.UFloat.Significand.NumWords)
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
    GpuBigFloat scalar)
    : FloatVectorBinaryScalarExpressionBase(
        vector,
        scalar,
        Math.Max(vector.WordsPerElement, scalar.UFloat.Significand.NumWords) + 1)
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
        if (count <= 0)
        {
            return new GpuBigFloatArray(workspace, 0, WordsPerElement);
        }

        return inner.Evaluate(workspace, _effectiveSkip + elementOffset, count, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal sealed class FloatVectorTakeExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression inner,
    int takeCount) : IGpuBigFloatVectorExpression
{
    public int Length { get; } = Math.Clamp(takeCount, 0, inner.Length);

    public int WordsPerElement => inner.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);
        if (effectiveCount <= 0)
        {
            return new GpuBigFloatArray(workspace, 0, WordsPerElement);
        }

        return inner.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => inner.Dispose();
}

internal sealed class FloatVectorWhereExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigFloatVectorExpression
{
    public int Length => source.Length;
    public int WordsPerElement => source.WordsPerElement;

    private GpuBigUIntArray _compactedSignificands;
    private GpuBigUIntArray _compactedExponents;
    private GpuBigUIntArray _compactedSigns;
    private GpuBigUIntArray _scanResult;
    private bool _allocated;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        if (targetResult != null)
        {
            throw new NotSupportedException("Where expression does not support targetResult because its size is dynamic.");
        }

        var evaluatedSource = source.Evaluate(workspace);
        var evaluatedPredicates = predicate.Evaluate(workspace);

        var originalCount = evaluatedSource.Length;
        if (originalCount == 0)
        {
            return new GpuBigFloatArray(workspace, 0, WordsPerElement);
        }

        if (!_allocated)
        {
            _scanResult = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedSignificands = new GpuBigUIntArray(workspace, originalCount, WordsPerElement);
            _compactedExponents = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedSigns = new GpuBigUIntArray(workspace, originalCount, 1);
            _allocated = true;
        }

        StreamCompactionOrchestrator.ExecuteExclusiveScan(
            workspace, _scanResult.DevicePointer, evaluatedPredicates.DevicePointer, originalCount);

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedSignificands.DevicePointer,
                evaluatedSource.Significands.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                WordsPerElement));

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedExponents.DevicePointer,
                evaluatedSource.Exponents.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                1));

        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedSigns.DevicePointer,
                evaluatedSource.Signs.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                1));

        var finalCount = elementCount ?? (originalCount - elementOffset);
        return new GpuBigFloatArray(
            _compactedSignificands.Slice(elementOffset, finalCount),
            _compactedExponents.Slice(elementOffset, finalCount),
            _compactedSigns.Slice(elementOffset, finalCount));
    }

    public void Dispose()
    {
        source.Dispose();
        predicate.Dispose();
        if (!_allocated)
        {
            return;
        }

        _scanResult.Dispose();
        _compactedSignificands.Dispose();
        _compactedExponents.Dispose();
        _compactedSigns.Dispose();
    }
}

internal enum FloatReductionKind
{
    Sum = 0,
    Max = 1,
    Min = 2
}

internal static class FloatVectorReductionOrchestrator
{
    private const int ChunkSize = 256;

    public static GpuBigFloat Reduce(
        GpuWorkspace workspace,
        GpuBigFloatArray evaluated,
        FloatReductionKind kind)
    {
        if (evaluated.Length == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        var numWords = evaluated.WordsPerElement;
        if (evaluated.Length == 1)
        {
            var first = evaluated.GetElement(0);
            var copySig = new GpuBigUInt(workspace, numWords);
            workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, first.UFloat.Significand.DevicePointer, numWords, workspace.Stream);
            return new GpuBigFloat(copySig, first.UFloat.Exponent, first.IsNegative);
        }

        var passes = new List<(int GridDimX, int BatchSize, int ChunkSize)>();
        var batchSize = evaluated.Length;
        while (batchSize > 1)
        {
            var nextBatchSize = (batchSize + ChunkSize - 1) / ChunkSize;
            passes.Add((GridDimX: nextBatchSize, BatchSize: batchSize, ChunkSize));
            batchSize = nextBatchSize;
        }

        var tempBuffers = new List<GpuBigFloatArray>();
        using var finalArray = new GpuBigFloatArray(workspace, 1, numWords);
        try
        {
            foreach (var pass in passes)
            {
                if (pass.GridDimX > 1)
                {
                    tempBuffers.Add(new GpuBigFloatArray(workspace, pass.GridDimX, numWords));
                }
            }

            var blockDimX = (uint)Math.Clamp(numWords, 32, 1024);
            var currentInput = evaluated;
            var tempIdx = 0;

            for (var i = 0; i < passes.Count; i++)
            {
                var pass = passes[i];
                var isLastPass = i == passes.Count - 1;
                var currentOutput = isLastPass ? finalArray : tempBuffers[tempIdx++];

                var chunkCtx = new GpuBigIntVectorKernels.AotContexts.VectorFloatReductionChunk
                {
                    A = currentInput.Significands.DevicePointer,
                    ExpA = currentInput.Exponents.DevicePointer,
                    SignA = currentInput.Signs.DevicePointer,
                    Result = currentOutput.Significands.DevicePointer,
                    ExpResult = currentOutput.Exponents.DevicePointer,
                    SignResult = currentOutput.Signs.DevicePointer,
                    NumWords = numWords,
                    BatchSize = pass.BatchSize,
                    ChunkSize = pass.ChunkSize
                };

                var gridDimX = (uint)pass.GridDimX;
                switch (kind)
                {
                    case FloatReductionKind.Sum:
                        GpuBigIntVectorKernels.LaunchVectorFloatSumReductionChunkKernelPtr(
                            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
                        break;
                    case FloatReductionKind.Max:
                        GpuBigIntVectorKernels.LaunchVectorFloatMaxReductionChunkKernelPtr(
                            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
                        break;
                    case FloatReductionKind.Min:
                        GpuBigIntVectorKernels.LaunchVectorFloatMinReductionChunkKernelPtr(
                            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
                        break;
                }

                currentInput = currentOutput;
            }

            var reduced = finalArray.GetElement(0);
            var resultSig = new GpuBigUInt(workspace, numWords);
            workspace.Provider.CopyDeviceToDevice(resultSig.DevicePointer, reduced.UFloat.Significand.DevicePointer, numWords, workspace.Stream);
            return new GpuBigFloat(resultSig, reduced.UFloat.Exponent, reduced.IsNegative);
        }
        finally
        {
            foreach (var temp in tempBuffers)
            {
                temp.Dispose();
            }
        }
    }
}
