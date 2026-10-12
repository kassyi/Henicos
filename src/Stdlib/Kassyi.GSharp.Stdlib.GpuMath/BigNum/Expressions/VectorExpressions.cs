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
    /// Evaluates the vectorized expression into a new or existing device array, optionally computing only a sub-slice.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for memory allocation and CUDA stream execution.</param>
    /// <param name="elementOffset">The starting element index to evaluate.</param>
    /// <param name="elementCount">The number of elements to evaluate. If null, evaluates to the end of the vector length.</param>
    /// <param name="targetResult">An optional destination array for the evaluated values. If provided, it must have a length of elementCount.</param>
    /// <returns>A <see cref="GpuBigIntArray"/> containing the evaluated vector slice results.</returns>
    GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null);
}

internal class GpuBigIntVectorLeafExpression(GpuBigIntArray array) : IGpuBigIntVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int count = elementCount ?? (Length - elementOffset);
        if (elementOffset == 0 && count == Length) return array;
        return array.Slice(elementOffset, count);
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

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorMulScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            vecResult.Signs.DevicePointer,
            (uint)(scalarResult.IsNegative ? 1 : 0),
            _finalSigns.DevicePointer,
            vector.WordsPerElement,
            scalarResult.Value.NumWords,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

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

internal class VectorAddScalarExpression([TakesOwnership] IGpuBigIntVectorExpression vector, [TakesOwnership] IGpuBigIntExpression scalar) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement => Math.Max(vector.WordsPerElement, scalar.NumWords) + 1;

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorAddScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            vecResult.Signs.DevicePointer,
            (uint)(scalarResult.IsNegative ? 1 : 0),
            _finalSigns.DevicePointer,
            vector.WordsPerElement,
            scalarResult.Value.NumWords,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

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

internal class VectorSubScalarExpression([TakesOwnership] IGpuBigIntVectorExpression vector, [TakesOwnership] IGpuBigIntExpression scalar) : IGpuBigIntVectorExpression
{
    public int Length => vector.Length;
    public int WordsPerElement => Math.Max(vector.WordsPerElement, scalar.NumWords);

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorSubScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            vecResult.Signs.DevicePointer,
            (uint)(scalarResult.IsNegative ? 1 : 0),
            _finalSigns.DevicePointer,
            vector.WordsPerElement,
            scalarResult.Value.NumWords,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

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

internal class VectorAddVectorExpression([TakesOwnership] IGpuBigIntVectorExpression left, [TakesOwnership] IGpuBigIntVectorExpression right) : IGpuBigIntVectorExpression
{
    public int Length => left.Length; 
    public int WordsPerElement => Math.Max(left.WordsPerElement, right.WordsPerElement) + 1;

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorZipAddKernelPtr(
            leftResult.Values.DevicePointer,
            rightResult.Values.DevicePointer,
            _finalValues.DevicePointer,
            leftResult.Signs.DevicePointer,
            rightResult.Signs.DevicePointer,
            _finalSigns.DevicePointer,
            leftResult.WordsPerElement,
            rightResult.WordsPerElement,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);


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
        left.Dispose();
        right.Dispose();
    }
}

internal class VectorSubVectorExpression([TakesOwnership] IGpuBigIntVectorExpression left, [TakesOwnership] IGpuBigIntVectorExpression right) : IGpuBigIntVectorExpression
{
    public int Length => left.Length;
    public int WordsPerElement => Math.Max(left.WordsPerElement, right.WordsPerElement);

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorZipSubKernelPtr(
            leftResult.Values.DevicePointer,
            rightResult.Values.DevicePointer,
            _finalValues.DevicePointer,
            leftResult.Signs.DevicePointer,
            rightResult.Signs.DevicePointer,
            _finalSigns.DevicePointer,
            leftResult.WordsPerElement,
            rightResult.WordsPerElement,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);


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
        left.Dispose();
        right.Dispose();
    }
}

internal class VectorMulVectorExpression([TakesOwnership] IGpuBigIntVectorExpression left, [TakesOwnership] IGpuBigIntVectorExpression right) : IGpuBigIntVectorExpression
{
    public int Length => left.Length;
    public int WordsPerElement => left.WordsPerElement + right.WordsPerElement;

    private GpuBigUIntArray _finalValues;
    private GpuBigUIntArray _finalSigns;
    private bool _allocated;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
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

        int gridDimX = evalCount;
        int blockDimX = Math.Min(1024, WordsPerElement);

        GpuBigIntVectorKernels.LaunchVectorZipMulKernelPtr(
            leftResult.Values.DevicePointer,
            rightResult.Values.DevicePointer,
            _finalValues.DevicePointer,
            leftResult.Signs.DevicePointer,
            rightResult.Signs.DevicePointer,
            _finalSigns.DevicePointer,
            leftResult.WordsPerElement,
            rightResult.WordsPerElement,
            WordsPerElement,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);


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
        left.Dispose();
        right.Dispose();
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

        IntPtr initialPtr = _tempBuffers[0].DevicePointer;
        GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToTwosComplementKernelPtr(
            vecResult.Values.DevicePointer,
            vecResult.Signs.DevicePointer,
            initialPtr,
            NumWords,
            _vector.Length,
            (uint)_vector.Length, 1, 1,
            (uint)Math.Min(1024, NumWords), 1, 1,
            0, workspace.Stream);

        IntPtr currentInputPtr = initialPtr;
        int tempBufferIndex = 1;

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

        bool isNegative = false;
        var cpuValue = _finalValue.Value.ToBigInteger();
        
        // Two's complement check: if MSB is set, it's negative
        if (cpuValue.Sign < 0 || (cpuValue > 0 && (cpuValue >> (NumWords * 32 - 1)) > 0))
        {
            isNegative = true;
            // CPU fallback for Two's Complement to Sign-Magnitude
            var shift = (System.Numerics.BigInteger)1 << (NumWords * 32);
            cpuValue = shift - cpuValue;
        }
        
        var bytes = cpuValue.ToByteArray();
        var uints = new uint[NumWords];
        Buffer.BlockCopy(bytes, 0, uints, 0, Math.Min(bytes.Length, uints.Length * 4));
        var finalMagnitude = new GpuBigUInt(workspace, uints);

        
        return new GpuBigInt(finalMagnitude, isNegative);
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

internal class VectorMaxReductionExpression : IGpuBigIntExpression, IDisposable
{
    private readonly IGpuBigIntVectorExpression _vector;
    public int NumWords => _vector.WordsPerElement;

    private GpuBigUInt? _finalValue;
    private readonly List<GpuBigUIntArray> _tempBuffers = new();
    private readonly List<(int GridDimX, int BatchSize, int ChunkSize)> _passes = new();
    private bool _allocated;

    public VectorMaxReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;

        int batchSize = vector.Length;
        int chunkSize = 256; 

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

        IntPtr initialPtr = _tempBuffers[0].DevicePointer;
        GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToOffsetBinaryKernelPtr(
            vecResult.Values.DevicePointer,
            vecResult.Signs.DevicePointer,
            initialPtr,
            NumWords,
            _vector.Length,
            (uint)_vector.Length, 1, 1,
            (uint)Math.Min(1024, NumWords), 1, 1,
            0, workspace.Stream);

        IntPtr currentInputPtr = initialPtr;
        int tempBufferIndex = 1;

        for (int i = 0; i < _passes.Count; i++)
        {
            var pass = _passes[i];
            bool isLastPass = (i == _passes.Count - 1);
            
            IntPtr currentOutputPtr = isLastPass 
                ? _finalValue.Value.DevicePointer 
                : _tempBuffers[tempBufferIndex++].DevicePointer;

            GpuBigIntVectorKernels.LaunchVectorMaxReductionChunkKernelPtr(
                currentInputPtr,
                currentOutputPtr,
                NumWords,
                pass.BatchSize,
                pass.ChunkSize,
                (uint)pass.GridDimX, 1, 1,
                (uint)Math.Min(1024, NumWords), 1, 1,
                0, workspace.Stream);

            currentInputPtr = currentOutputPtr; 
        }

        bool isNegative = false;
        var cpuValue = _finalValue.Value.ToBigInteger();
        
        // Offset Binary check: Un-bias the MSB
        var msbMask = (System.Numerics.BigInteger)1 << (NumWords * 32 - 1);
        cpuValue ^= msbMask;
        
        // Two's complement check: if MSB is set, it's negative
        if (cpuValue.Sign < 0 || (cpuValue > 0 && (cpuValue >> (NumWords * 32 - 1)) > 0))
        {
            isNegative = true;
            // CPU fallback for Two's Complement to Sign-Magnitude
            var shift = (System.Numerics.BigInteger)1 << (NumWords * 32);
            cpuValue = shift - cpuValue;
        }
        
        var bytes = cpuValue.ToByteArray();
        var uints = new uint[NumWords];
        Buffer.BlockCopy(bytes, 0, uints, 0, Math.Min(bytes.Length, uints.Length * 4));
        var finalMagnitude = new GpuBigUInt(workspace, uints);

        
        return new GpuBigInt(finalMagnitude, isNegative);
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

internal class VectorMinReductionExpression : IGpuBigIntExpression, IDisposable
{
    private readonly IGpuBigIntVectorExpression _vector;
    public int NumWords => _vector.WordsPerElement;

    private GpuBigUInt? _finalValue;
    private readonly List<GpuBigUIntArray> _tempBuffers = new();
    private readonly List<(int GridDimX, int BatchSize, int ChunkSize)> _passes = new();
    private bool _allocated;

    public VectorMinReductionExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;

        int batchSize = vector.Length;
        int chunkSize = 256; 

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

        IntPtr initialPtr = _tempBuffers[0].DevicePointer;
        GpuBigIntVectorKernels.LaunchVectorSignMagnitudeToOffsetBinaryKernelPtr(
            vecResult.Values.DevicePointer,
            vecResult.Signs.DevicePointer,
            initialPtr,
            NumWords,
            _vector.Length,
            (uint)_vector.Length, 1, 1,
            (uint)Math.Min(1024, NumWords), 1, 1,
            0, workspace.Stream);

        IntPtr currentInputPtr = initialPtr;
        int tempBufferIndex = 1;

        for (int i = 0; i < _passes.Count; i++)
        {
            var pass = _passes[i];
            bool isLastPass = (i == _passes.Count - 1);
            
            IntPtr currentOutputPtr = isLastPass 
                ? _finalValue.Value.DevicePointer 
                : _tempBuffers[tempBufferIndex++].DevicePointer;

            GpuBigIntVectorKernels.LaunchVectorMinReductionChunkKernelPtr(
                currentInputPtr,
                currentOutputPtr,
                NumWords,
                pass.BatchSize,
                pass.ChunkSize,
                (uint)pass.GridDimX, 1, 1,
                (uint)Math.Min(1024, NumWords), 1, 1,
                0, workspace.Stream);

            currentInputPtr = currentOutputPtr; 
        }

        bool isNegative = false;
        var cpuValue = _finalValue.Value.ToBigInteger();
        
        // Offset Binary check: Un-bias the MSB
        var msbMask = (System.Numerics.BigInteger)1 << (NumWords * 32 - 1);
        cpuValue ^= msbMask;
        
        // Two's complement check: if MSB is set, it's negative
        if (cpuValue.Sign < 0 || (cpuValue > 0 && (cpuValue >> (NumWords * 32 - 1)) > 0))
        {
            isNegative = true;
            // CPU fallback for Two's Complement to Sign-Magnitude
            var shift = (System.Numerics.BigInteger)1 << (NumWords * 32);
            cpuValue = shift - cpuValue;
        }
        
        var bytes = cpuValue.ToByteArray();
        var uints = new uint[NumWords];
        Buffer.BlockCopy(bytes, 0, uints, 0, Math.Min(bytes.Length, uints.Length * 4));
        var finalMagnitude = new GpuBigUInt(workspace, uints);

        
        return new GpuBigInt(finalMagnitude, isNegative);
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

internal class VectorCountExpression : IGpuBigIntExpression, IDisposable
{
    private readonly IGpuBigIntVectorExpression _vector;
    public int NumWords => 1;

    public VectorCountExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    {
        _vector = vector;
    }

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        int count = _vector.Length;
        _vector.Dispose(); // Skip GPU evaluation (O(1) optimization), but free resources

        if (targetResult.HasValue)
        {
            var countVal = new uint[targetResult.Value.NumWords];
            countVal[0] = (uint)count;
            workspace.Provider.CopyHostToDevice(targetResult.Value.DevicePointer, countVal, countVal.Length, workspace.Stream);
            return new GpuBigInt(targetResult.Value, false);
        }
        else
        {
            var countVal = new uint[] { (uint)count };
            return new GpuBigInt(new GpuBigUInt(workspace, countVal), false);
        }
    }

    public void Dispose()
    {
        _vector.Dispose();
    }
}

internal class VectorTakeExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count) : IGpuBigIntVectorExpression
{
    public int Length => Math.Min(source.Length, count);
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
        
        // Ensure we do not evaluate beyond the Take boundary
        int effectiveCount = Math.Min(evalCount, Length - elementOffset);
        
        if (effectiveCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }

        return source.Evaluate(workspace, elementOffset, effectiveCount, targetResult);
    }

    public void Dispose()
    {
        source.Dispose();
    }
}

internal class VectorSkipExpression([TakesOwnership] IGpuBigIntVectorExpression source, int count) : IGpuBigIntVectorExpression
{
    private readonly int _skipCount = Math.Min(count, source.Length);

    public int Length => source.Length - _skipCount;
    public int WordsPerElement => source.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null, GpuBigUIntArray? targetResult = null)
    {
        int evalCount = elementCount ?? (Length - elementOffset);
        
        if (evalCount <= 0)
        {
            return new GpuBigIntArray(
                new GpuBigUIntArray(workspace, 0, WordsPerElement),
                new GpuBigUIntArray(workspace, 0, 1));
        }
        
        // Pass the requested element slice but shifted by the Skip count
        return source.Evaluate(workspace, elementOffset + _skipCount, evalCount, targetResult);
    }

    public void Dispose()
    {
        source.Dispose();
    }
}
