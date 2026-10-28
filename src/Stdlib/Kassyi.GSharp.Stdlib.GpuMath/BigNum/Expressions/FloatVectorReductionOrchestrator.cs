namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal enum FloatReductionKind
{
    Sum = 0,
    Max = 1,
    Min = 2
}

internal static class FloatVectorReductionOrchestrator
{
    private const int ChunkSize = 256;

    /// <summary>
    /// Reduces <paramref name="source"/>. An empty vector is decided before it is evaluated, so nothing is launched
    /// for it.
    /// </summary>
    public static GpuBigFloat Reduce(
        GpuWorkspace workspace,
        IGpuBigFloatVectorExpression source,
        FloatReductionKind kind)
    {
        if (source.Length == 0)
        {
            return ReduceEmpty(workspace, source.WordsPerElement, kind);
        }

        return Reduce(workspace, source.Evaluate(workspace), kind);
    }

    // As System.Linq: the sum of nothing is +0; Max / Min of nothing has no value.
    private static GpuBigFloat ReduceEmpty(GpuWorkspace workspace, int numWords, FloatReductionKind kind)
    {
        if (kind == FloatReductionKind.Sum)
        {
            var zero = new GpuBigUInt(workspace, numWords);
            workspace.Provider.ClearMemoryAsync(zero.DevicePointer, numWords, workspace.Stream);
            return new GpuBigFloat(zero, 0, false);
        }

        throw new InvalidOperationException("Sequence contains no elements");
    }

    public static GpuBigFloat Reduce(
        GpuWorkspace workspace,
        GpuBigFloatArray evaluated,
        FloatReductionKind kind)
    {
        var numWords = evaluated.WordsPerElement;
        if (evaluated.Length == 0)
        {
            return ReduceEmpty(workspace, numWords, kind);
        }

        if (evaluated.Length == 1)
        {
            var first = evaluated.GetElement(0);
            var copySig = new GpuBigUInt(workspace, numWords);
            workspace.Provider.CopyDeviceToDevice(copySig.DevicePointer, first.Significand.DevicePointer, numWords, workspace.Stream);
            return new GpuBigFloat(copySig, first.Exponent, first.IsNegative);
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

            // Whole warps: the block-wide max reductions of the chunk kernels assume no partial warp.
            var blockDimX = (uint)Math.Min(1024, (numWords + 31) & ~31);
            var currentInput = evaluated;
            var tempIdx = 0;

            for (var i = 0; i < passes.Count; i++)
            {
                var pass = passes[i];
                var isLastPass = i == passes.Count - 1;
                var currentOutput = isLastPass ? finalArray : tempBuffers[tempIdx++];

                var chunkCtx = new GpuBigIntVectorKernels.AotContexts.VectorFloatReductionChunk
                {
                    Input = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
                    {
                        Limbs = currentInput.Significands.DevicePointer,
                        Exponents = currentInput.Exponents.DevicePointer,
                        Signs = currentInput.Signs.DevicePointer,
                        NumWords = numWords
                    },
                    Output = new GpuBigIntVectorKernels.AotContexts.FloatVectorOutput
                    {
                        Limbs = currentOutput.Significands.DevicePointer,
                        Exponents = currentOutput.Exponents.DevicePointer,
                        Signs = currentOutput.Signs.DevicePointer,
                        NumWords = numWords
                    },
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
            workspace.Provider.CopyDeviceToDevice(resultSig.DevicePointer, reduced.Significand.DevicePointer, numWords, workspace.Stream);
            return new GpuBigFloat(resultSig, reduced.Exponent, reduced.IsNegative);
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
