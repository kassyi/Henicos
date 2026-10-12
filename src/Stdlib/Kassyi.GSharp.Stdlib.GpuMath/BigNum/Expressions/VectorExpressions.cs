using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

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
    /// Evaluates the vectorized expression into a new or existing device array.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="targetResult">An optional destination array for the evaluated values.</param>
    /// <returns>A <see cref="GpuBigIntArray"/> containing the evaluated vector results.</returns>
    GpuBigIntArray Evaluate(GpuWorkspace workspace, GpuBigUIntArray? targetResult = null);
}

internal class GpuBigIntVectorLeafExpression(GpuBigIntArray array) : IGpuBigIntVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, GpuBigUIntArray? targetResult = null)
    {
        return array;
    }

    public void Dispose()
    {
        // Does not own the array
    }
}

internal class VectorMulScalarExpression([TakesOwnership] IGpuBigIntVectorExpression vector, [TakesOwnership] IGpuBigIntExpression scalar) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement => vector.WordsPerElement + scalar.NumWords; // Naive length expansion for mul

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, GpuBigUIntArray? targetResult = null)
    {
        var vecResult = vector.Evaluate(workspace);
        var scalarResult = scalar.Evaluate(workspace);

        if (targetResult != null)
        {
            _finalValues = targetResult.Value;
        }
        else if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, WordsPerElement);
            _finalSigns = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        int gridDimX = Length;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorMulScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        workspace.Provider.ClearMemoryAsync(_finalSigns.DevicePointer, Length, workspace.Stream);

        return new GpuBigIntArray(_finalValues, _finalSigns);
    }

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

internal class VectorSumReductionExpression : IGpuBigIntExpression, IDisposable
{
    private readonly IGpuBigIntVectorExpression _vector;
    public int NumWords => _vector.WordsPerElement;

    private GpuBigUInt? _finalValue;
    private readonly List<GpuBigUIntArray> _tempBuffers = new();
    private readonly List<(int GridDimX, int BatchSize, int ChunkSize)> _passes = new();
    private bool _allocated;

    public VectorSumReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;

        // Pre-calculate tree reduction passes sizes
        int batchSize = vector.Length;
        int chunkSize = 256; // Tune this: number of elements each block will reduce

        while (batchSize > 1)
        {
            int nextBatchSize = (batchSize + chunkSize - 1) / chunkSize;
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
            // Zero or one element
            if (_vector.Length == 1)
            {
                // Just copy the single element
                workspace.Provider.CopyDeviceToDevice(
                    vecResult.Values.DevicePointer, 
                    _finalValue.Value.DevicePointer, 
                    NumWords * 4, 
                    workspace.Stream);
            }
            else
            {
                // Zero elements
                workspace.Provider.ClearMemoryAsync(_finalValue.Value.DevicePointer, NumWords, workspace.Stream);
            }
            return new GpuBigInt(_finalValue.Value, false);
        }

        IntPtr currentInputPtr = vecResult.Values.DevicePointer;
        int tempBufferIndex = 0;

        for (int i = 0; i < _passes.Count; i++)
        {
            var pass = _passes[i];
            bool isLastPass = (i == _passes.Count - 1);
            
            IntPtr currentOutputPtr = isLastPass 
                ? _finalValue.Value.DevicePointer 
                : _tempBuffers[tempBufferIndex++].DevicePointer;

            GpuBigIntVectorKernels.LaunchVectorSumReductionChunkKernelPtr(
                currentInputPtr,
                currentOutputPtr,
                NumWords,
                pass.BatchSize,
                pass.ChunkSize,
                (uint)pass.GridDimX, 1, 1,
                (uint)Math.Min(1024, NumWords), 1, 1,
                0, workspace.Stream);

            currentInputPtr = currentOutputPtr; // Output becomes input for next pass
        }

        return new GpuBigInt(_finalValue.Value, false);
    }

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
