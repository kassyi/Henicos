
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Integer Sum / Max / Min over a vector: converts the elements to (N + 1)-word two's complement (offset binary for
/// Max / Min), reduces them in passes of <see cref="ChunkSize"/> elements, and lets a finalize kernel write the
/// magnitude into the destination and a 2-word header (<c>[0]</c>: bit 0 = negative, bit 1 = the magnitude does not
/// fit in N words; <c>[1]</c>: the number of reduced elements), which is the only readback.
/// </summary>
internal abstract class VectorReductionExpressionBase : IGpuBigIntExpression, IDisposable
{
    private const int ChunkSize = 256;

    private readonly IGpuBigIntVectorExpression _vector;

    // One entry per pass: the pass reduces up to BatchSize elements into GridDimX partial results. Built from the
    // capacity, with one pass even for a single element, so that every non-empty input goes through the finalize
    // kernel. With a device count each pass derives its own batch on the device, and the blocks past it write nothing.
    private readonly List<(int GridDimX, int BatchSize)> _passes = [];

    // [0]: the converted elements; [1 + i]: the output of pass i. All (N + 1) words per element.
    private readonly List<GpuBigUIntArray> _buffers = [];
    private readonly uint[] _headerHost = new uint[2];
    private GpuBigUInt? _header;

    public int NumWords => _vector.WordsPerElement;

    /// <summary>
    /// The width of the accumulator: one word more than the elements, so that the magnitude of every element, and of
    /// any sum of fewer than 2^31 elements, fits in two's complement.
    /// </summary>
    private int AccumulatorWords => NumWords + 1;

    protected abstract bool UseOffsetBinary { get; }

    /// <summary>
    /// The reduction of an empty vector throws <see cref="InvalidOperationException"/> (Max / Min), as
    /// <c>System.Linq</c> does; otherwise it is 0 (Sum).
    /// </summary>
    protected abstract bool EmptyIsError { get; }

    protected VectorReductionExpressionBase([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;

        var batchSize = vector.Capacity;
        if (batchSize > 0)
        {
            do
            {
                var nextBatchSize = (batchSize + ChunkSize - 1) / ChunkSize;
                _passes.Add((GridDimX: nextBatchSize, BatchSize: batchSize));
                batchSize = nextBatchSize;
            }
            while (batchSize > 1);
        }
    }

    /// <summary>
    /// Evaluates the reduction. The magnitude is written into <paramref name="targetResult"/>, or into a new buffer
    /// that the caller owns when it is <see langword="null"/>; the returned value wraps that buffer.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetResult"/> does not have <see cref="NumWords"/> words.
    /// </exception>
    /// <exception cref="OverflowException">The final sum does not fit in <see cref="NumWords"/> words.</exception>
    /// <exception cref="InvalidOperationException">The vector is empty and the reduction is Max or Min.</exception>
    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (targetResult is { } target && target.NumWords != NumWords)
        {
            throw new ArgumentException($"Destination buffer must have {NumWords} words.", nameof(targetResult));
        }

        // An empty vector has no passes. Max / Min have no value to return; stop before anything is launched.
        if (_passes.Count == 0 && EmptyIsError)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }

        // A static empty vector is not evaluated: no element is read, and its nodes would launch empty grids.
        GpuBigIntArrayView vecResult = default;
        var count = DeviceElementCount.Static(0);
        if (_passes.Count > 0)
        {
            var evaluated = DeviceCounted.Evaluate(_vector, workspace);
            if (evaluated.Storage.WordsPerElement != NumWords)
            {
                // The kernels read the elements, and size the accumulator and the result, with NumWords.
                throw new InvalidOperationException(
                    $"The vector evaluated to {evaluated.Storage.WordsPerElement} words per element, not {NumWords}.");
            }

            vecResult = evaluated.Storage;
            count = evaluated.Count;
        }

        var destination = targetResult ?? new GpuBigUInt(workspace, NumWords);
        try
        {
            // An empty Sum also goes through the finalize kernel, which writes +0 and a count of 0.
            EnsureBuffers(workspace);
            var header = _header ?? throw new InvalidOperationException("The reduction header was not allocated.");
            Reduce(workspace, vecResult, count, destination, header);

            workspace.Provider.CopyDeviceToHost(_headerHost, header.DevicePointer, 2, workspace.Stream);
            if (_headerHost[1] == 0u && EmptyIsError)
            {
                // Reached when the count is known only on the device (after Where); a static empty vector stopped
                // above.
                throw new InvalidOperationException("Sequence contains no elements");
            }

            if ((_headerHost[0] & 2u) != 0u)
            {
                throw new OverflowException($"The sum does not fit in {NumWords} words.");
            }

            return new GpuBigInt(destination, (_headerHost[0] & 1u) != 0u);
        }
        catch when (!targetResult.HasValue)
        {
            destination.Dispose();
            throw;
        }
    }

    private void EnsureBuffers(GpuWorkspace workspace)
    {
        if (_header.HasValue)
        {
            return;
        }

        // Allocate everything first and keep it only when every allocation succeeded, so that a failed allocation
        // leaves nothing half-built behind for the next evaluation.
        var buffers = new List<GpuBigUIntArray>(_passes.Count + 1);
        try
        {
            buffers.Add(new GpuBigUIntArray(workspace, _vector.Capacity, AccumulatorWords));
            foreach (var pass in _passes)
            {
                buffers.Add(new GpuBigUIntArray(workspace, pass.GridDimX, AccumulatorWords));
            }

            var header = new GpuBigUInt(workspace, 2);
            _buffers.AddRange(buffers);
            _header = header;
        }
        catch
        {
            foreach (var buffer in buffers)
            {
                buffer.Dispose();
            }

            throw;
        }
    }

    private void Reduce(GpuWorkspace workspace, GpuBigIntArrayView vecResult, DeviceElementCount count,
        GpuBigUInt destination, GpuBigUInt header)
    {
        // Whole warps: the block-wide max reductions of the chunk kernels combine lanes of full warps only.
        var blockDimX = (uint)Math.Min(1024, (AccumulatorWords + 31) & ~31);

        // An empty vector (the empty vecResult) has nothing to convert and no passes (a grid of 0 blocks is an invalid
        // launch); the finalize kernel does not read its source when the count is 0.
        if (!vecResult.IsEmpty)
        {
            var source = vecResult;
            var convertCtx = new GpuBigIntVectorKernels.AotContexts.VectorSignConvert
            {
                Source = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = source.Values.DevicePointer,
                    Signs = source.Signs.DevicePointer,
                    NumWords = NumWords
                },
                Dest = _buffers[0].DevicePointer,
                DestWords = AccumulatorWords,
                BatchSize = source.Length
            };

            // The whole capacity is converted; the passes read only the elements
            if (UseOffsetBinary)
            {
                GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToOffsetBinaryKernelPtr(
                    convertCtx, (uint)source.Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
            }
            else
            {
                GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToTwosComplementKernelPtr(
                    convertCtx, (uint)source.Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
            }
        }

        var countContext = count.ToContext();
        var currentInputPtr = _buffers[0].DevicePointer;
        for (var i = 0; i < _passes.Count; i++)
        {
            var pass = _passes[i];
            var currentOutputPtr = _buffers[i + 1].DevicePointer;
            var chunkCtx = new GpuBigIntVectorKernels.AotContexts.VectorReductionChunk
            {
                A = currentInputPtr,
                Result = currentOutputPtr,
                NumWords = AccumulatorWords,
                Count = countContext,
                PassIndex = i,
                ChunkSize = ChunkSize
            };

            DispatchChunkKernel(workspace, chunkCtx, (uint)pass.GridDimX, blockDimX);
            currentInputPtr = currentOutputPtr;
        }

        var finalizeCtx = new GpuBigIntVectorKernels.AotContexts.VectorReductionFinalize
        {
            Source = currentInputPtr,
            Destination = destination.DevicePointer,
            Header = header.DevicePointer,
            SourceWords = AccumulatorWords,
            ResultWords = NumWords,
            Count = countContext,
            OffsetBinary = UseOffsetBinary
        };

        GpuBigIntVectorKernels.LaunchVectorReductionFinalizeKernelPtr(
            finalizeCtx, 1, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }

    protected abstract void DispatchChunkKernel(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorReductionChunk chunkCtx,
        uint gridDimX,
        uint blockDimX);

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        if (_header.HasValue)
        {
            _header.Value.Dispose();
            _header = null;
        }

        _vector.Dispose();
    }
}

internal sealed class VectorSumReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : VectorReductionExpressionBase(vector)
{
    protected override bool UseOffsetBinary => false;

    protected override bool EmptyIsError => false;

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

    protected override bool EmptyIsError => true;

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

    protected override bool EmptyIsError => true;

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

    // A length known on the host needs no evaluation; a device count (after Where) evaluates the vector and lets a
    // kernel write the count, so that no host synchronization is needed and a CUDA graph counts again on every launch
    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var count = vector.TryGetLength(out var length)
            ? DeviceElementCount.Static(length)
            : DeviceCounted.Evaluate(vector, workspace).Count;

        var destination = targetResult ?? new GpuBigUInt(workspace, NumWords);
        try
        {
            var ctx = new GpuBigIntVectorKernels.AotContexts.ElementCountWrite
            {
                Destination = destination.DevicePointer,
                DestinationWords = destination.NumWords,
                Count = count.ToContext()
            };
            GpuBigIntVectorKernels.LaunchWriteElementCountKernelPtr(ctx, 1, 1, 1, 32, 1, 1, 0, workspace.Stream);
            return new GpuBigInt(destination, false);
        }
        catch when (!targetResult.HasValue)
        {
            destination.Dispose();
            throw;
        }
    }

    public void Dispose() => vector.Dispose();
}
