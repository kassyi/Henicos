using System.Numerics;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
