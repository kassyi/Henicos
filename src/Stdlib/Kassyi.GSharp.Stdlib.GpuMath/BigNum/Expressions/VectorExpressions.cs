using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an expression node that evaluates to a contiguous array of GPU big integers.
/// </summary>
public interface IGpuBigIntVectorExpression : IDisposable
{
    /// <summary>
    /// Gets the number of elements in the vector.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    int WordsPerElement { get; }

    /// <summary>
    /// Evaluates the vectorized expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">An optional destination array for the evaluated values. If provided, it must have a length of elementCount.</param>
    /// <returns>A <see cref="GpuBigIntArray"/> containing the evaluated vector slice results.</returns>
    GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null);
}

internal class GpuBigIntVectorLeafExpression(GpuBigIntArray array) : IGpuBigIntVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        if (elementOffset == 0 && count == Length)
        {
            return array;
        }

        return array.Slice(elementOffset, count);
    }

    public void Dispose()
    {
        // Does not own the array
    }
}

internal readonly record struct VectorScalarOperands(
    GpuBigIntArray Vector,
    GpuBigInt Scalar,
    GpuBigIntArray Destination);

internal readonly record struct VectorZipOperands(
    GpuBigIntArray Left,
    GpuBigIntArray Right,
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
        var ctx = CreateScalarContext<GpuBigIntVectorKernels.AotContexts.VectorDivMod>(operands);
        ctx.IsModulo = true;
        GpuBigIntVectorKernels.LaunchVectorModScalarKernelPtr(
            ctx, gridDimX, 1, 1, 32, 1, 1, 0, workspace.Stream);
    }
}

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

internal abstract class VectorReductionExpressionBase : IGpuBigIntExpression, IDisposable
{
    private readonly IGpuBigIntVectorExpression _vector;
    public int NumWords => _vector.WordsPerElement;

    private GpuBigUInt? _finalValue;
    private readonly List<GpuBigUIntArray> _tempBuffers = [];
    private readonly List<(int GridDimX, int BatchSize, int ChunkSize)> _passes = [];
    private bool _allocated;

    protected abstract bool UseOffsetBinary { get; }

    protected VectorReductionExpressionBase([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;

        var batchSize = vector.Length;
        const int chunkSize = 256;

        while (batchSize > 1)
        {
            var nextBatchSize = (batchSize + chunkSize - 1) / chunkSize;
            _passes.Add((GridDimX: nextBatchSize, BatchSize: batchSize, ChunkSize: chunkSize));
            batchSize = nextBatchSize;
        }
    }

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var vecResult = _vector.Evaluate(workspace);

        if (targetResult.HasValue)
        {
            _finalValue = targetResult.Value;
        }
        else if (!_finalValue.HasValue)
        {
            _finalValue = new GpuBigUInt(workspace, NumWords);
        }

        if (!_allocated)
        {
            _tempBuffers.Add(new GpuBigUIntArray(workspace, _vector.Length, NumWords));
            foreach (var pass in _passes)
            {
                if (pass.GridDimX > 1)
                {
                    _tempBuffers.Add(new GpuBigUIntArray(workspace, pass.GridDimX, NumWords));
                }
            }

            _allocated = true;
        }

        if (_passes.Count == 0)
        {
            if (_vector.Length == 1)
            {
                workspace.Provider.CopyDeviceToDevice(
                    vecResult.Values.DevicePointer,
                    _finalValue.Value.DevicePointer,
                    NumWords * 4,
                    workspace.Stream);
            }
            else
            {
                workspace.Provider.ClearMemoryAsync(_finalValue.Value.DevicePointer, NumWords, workspace.Stream);
            }

            return new GpuBigInt(_finalValue.Value, false);
        }

        var initialPtr = _tempBuffers[0].DevicePointer;
        var blockDimX = (uint)Math.Min(1024, NumWords);
        var convertCtx = new GpuBigIntVectorKernels.AotContexts.VectorSignConvert
        {
            Src = vecResult.Values.DevicePointer,
            SignSrc = vecResult.Signs.DevicePointer,
            Dest = initialPtr,
            NumWords = NumWords,
            BatchSize = _vector.Length
        };

        if (UseOffsetBinary)
        {
            GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToOffsetBinaryKernelPtr(
                convertCtx, (uint)_vector.Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
        }
        else
        {
            GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToTwosComplementKernelPtr(
                convertCtx, (uint)_vector.Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
        }

        var currentInputPtr = initialPtr;
        var tempBufferIndex = 1;

        for (var i = 0; i < _passes.Count; i++)
        {
            var pass = _passes[i];
            var isLastPass = (i == _passes.Count - 1);

            var currentOutputPtr = isLastPass
                ? _finalValue.Value.DevicePointer
                : _tempBuffers[tempBufferIndex++].DevicePointer;

            var chunkCtx = new GpuBigIntVectorKernels.AotContexts.VectorReductionChunk
            {
                A = currentInputPtr,
                Result = currentOutputPtr,
                NumWords = NumWords,
                BatchSize = pass.BatchSize,
                ChunkSize = pass.ChunkSize
            };

            DispatchChunkKernel(workspace, chunkCtx, (uint)pass.GridDimX, blockDimX);
            currentInputPtr = currentOutputPtr;
        }

        var isNegative = false;
        var cpuValue = _finalValue.Value.ToBigInteger();

        if (UseOffsetBinary)
        {
            var msbMask = (BigInteger)1 << (NumWords * 32 - 1);
            cpuValue ^= msbMask;
        }

        if (cpuValue.Sign < 0 || (cpuValue > 0 && (cpuValue >> (NumWords * 32 - 1)) > 0))
        {
            isNegative = true;
            var shift = (BigInteger)1 << (NumWords * 32);
            cpuValue = shift - cpuValue;
        }

        var bytes = cpuValue.ToByteArray();
        var uints = new uint[NumWords];
        Buffer.BlockCopy(bytes, 0, uints, 0, Math.Min(bytes.Length, uints.Length * 4));
        var finalMagnitude = new GpuBigUInt(workspace, uints);

        return new GpuBigInt(finalMagnitude, isNegative);
    }

    protected abstract void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX);

    public void Dispose()
    {
        if (_finalValue.HasValue)
        {
            _finalValue.Value.Dispose();
            _finalValue = null;
        }

        foreach (var buffer in _tempBuffers)
        {
            buffer.Dispose();
        }

        _tempBuffers.Clear();
        _allocated = false;
        _vector.Dispose();
    }
}

internal sealed class VectorSumReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : VectorReductionExpressionBase(vector)
{
    protected override bool UseOffsetBinary => false;

    protected override void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX)
    {
        GpuBigIntVectorKernels.LaunchVectorSumReductionChunkKernelPtr(
            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorMaxReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : VectorReductionExpressionBase(vector)
{
    protected override bool UseOffsetBinary => true;

    protected override void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX)
    {
        GpuBigIntVectorKernels.LaunchVectorMaxReductionChunkKernelPtr(
            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal sealed class VectorMinReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : VectorReductionExpressionBase(vector)
{
    protected override bool UseOffsetBinary => true;

    protected override void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX)
    {
        GpuBigIntVectorKernels.LaunchVectorMinReductionChunkKernelPtr(
            chunkCtx, gridDimX, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }
}

internal class VectorCountExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : IGpuBigIntExpression, IDisposable
{
    public int NumWords => 1;

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var count = vector.Length;
        vector.Dispose(); // Skip GPU evaluation (O(1) optimization), but free resources

        if (targetResult.HasValue)
        {
            var countVal = new uint[targetResult.Value.NumWords];
            countVal[0] = (uint)count;
            workspace.Provider.CopyHostToDevice(targetResult.Value.DevicePointer, countVal, countVal.Length,
                workspace.Stream);
            return new GpuBigInt(targetResult.Value, false);
        }
        else
        {
            var countVal = new[] { (uint)count };
            return new GpuBigInt(new GpuBigUInt(workspace, countVal), false);
        }
    }

    public void Dispose() => vector.Dispose();
}

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    public int Length => Math.Min(source.Length, count);
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        // Ensure we do not evaluate beyond the Take boundary
        var effectiveCount = Math.Min(evalCount, Length - elementOffset);

        if (effectiveCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        return source.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count)
    : IGpuBigIntVectorExpression
{
    private readonly int _skipCount = Math.Min(count, source.Length);

    public int Length => source.Length - _skipCount;
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var evalCount = elementCount ?? (Length - elementOffset);

        if (evalCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        // Pass the requested element slice but shifted by the Skip count
        return source.Evaluate(workspace, elementOffset + _skipCount, evalCount, targetResult);
    }

    public void Dispose() => source.Dispose();
}

internal class VectorWhereExpression(
    [TakesOwnership] IGpuBigIntVectorExpression source,
    [TakesOwnership] IGpuBoolVectorExpression predicate) : IGpuBigIntVectorExpression
{
    public int Length => source.Length;
    public int WordsPerElement => source.WordsPerElement;

    private GpuBigUIntArray _compactedValues;
    private GpuBigUIntArray _compactedSigns;
    private GpuBigUIntArray _scanResult;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        if (targetResult != null)
        {
            throw new NotSupportedException(
                "Where expression does not support targetResult because its size is dynamic.");
        }

        var evaluatedSource = source.Evaluate(workspace);
        var evaluatedPredicates = predicate.Evaluate(workspace);

        var originalCount = evaluatedSource.Length;
        if (originalCount == 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        if (!_allocated)
        {
            _scanResult = new GpuBigUIntArray(workspace, originalCount, 1);
            _compactedValues = new GpuBigUIntArray(workspace, originalCount, WordsPerElement);
            _compactedSigns = new GpuBigUIntArray(workspace, originalCount, 1);
            _allocated = true;
        }

        // 1. Exclusive Scan on Predicates
        StreamCompactionOrchestrator.ExecuteExclusiveScan(
            workspace, _scanResult.DevicePointer, evaluatedPredicates.DevicePointer, originalCount);

        // 2. Scatter (Compact) Data
        StreamCompactionOrchestrator.CompactArray(
            workspace,
            new StreamCompactionOrchestrator.StreamCompactionRequest(
                _compactedValues.DevicePointer,
                evaluatedSource.Values.DevicePointer,
                evaluatedPredicates.DevicePointer,
                _scanResult.DevicePointer,
                originalCount,
                WordsPerElement));

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
        return new GpuBigIntArray(
            _compactedValues.Slice(elementOffset, finalCount),
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
        _compactedValues.Dispose();
        _compactedSigns.Dispose();
    }
}
