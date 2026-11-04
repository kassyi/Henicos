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
        FloatReductionKind kind) =>
        ReduceWithCount(workspace, source, kind, out _);

    /// <summary>
    /// Reduces the elements of <paramref name="source"/> and gives their number, from one evaluation (Average divides
    /// the sum by it). After a <c>Where</c> the number is read from the device: one synchronization, as the reduction
    /// reads its result back anyway.
    /// </summary>
    public static GpuBigFloat ReduceWithCount(
        GpuWorkspace workspace,
        IGpuBigFloatVectorExpression source,
        FloatReductionKind kind,
        out int count)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        if (source.Capacity == 0)
        {
            count = 0;
            return ReduceEmpty(workspace, source.WordsPerElement, kind);
        }

        var evaluated = DeviceCounted.Evaluate(source, workspace);
        count = evaluated.Elements(workspace);
        return Reduce(workspace, evaluated.Storage.Slice(0, count), kind);
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
        GpuBigFloatArrayView evaluated,
        FloatReductionKind kind)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        var numWords = evaluated.WordsPerElement;
        if (evaluated.Length == 0)
        {
            return ReduceEmpty(workspace, numWords, kind);
        }

        if (evaluated.Length == 1)
        {
            return CopyElement(workspace, evaluated.GetElement(0));
        }

        var passes = ReductionPasses.Plan(evaluated.Length, ChunkSize);
        var tempBuffers = new List<GpuBigFloatArray>();
        using var finalArray = new GpuBigFloatArray(workspace, 1, numWords);
        try
        {
            // Every pass but the last has more than one partial result; the last writes finalArray
            for (var i = 0; i < passes.Count - 1; i++)
            {
                tempBuffers.Add(new GpuBigFloatArray(workspace, passes[i].GridDimX, numWords));
            }

            // Whole warps: the block-wide max reductions of the chunk kernels assume no partial warp.
            var blockDimX = (uint)Math.Min(1024, (numWords + 31) & ~31);
            var currentInput = evaluated;
            for (var i = 0; i < passes.Count; i++)
            {
                GpuBigFloatArrayView currentOutput = i == passes.Count - 1 ? finalArray : tempBuffers[i];
                LaunchChunk(workspace, kind, currentInput, currentOutput, passes[i], blockDimX);
                currentInput = currentOutput;
            }

            return CopyElement(workspace, finalArray.GetElement(0));
        }
        finally
        {
            foreach (var temp in tempBuffers)
            {
                temp.Dispose();
            }
        }
    }

    // A new value that the caller owns, with the significand of the element copied on the device
    private static GpuBigFloat CopyElement(GpuWorkspace workspace, GpuBigFloatView element)
    {
        var numWords = element.Significand.NumWords;
        var significand = new GpuBigUInt(workspace, numWords);
        workspace.Provider.CopyDeviceToDevice(significand.DevicePointer, element.Significand.DevicePointer, numWords,
            workspace.Stream);
        return new GpuBigFloat(significand, element.Exponent, element.IsNegative);
    }

    // One pass: reduces each chunk of ChunkSize inputs into one partial result
    private static void LaunchChunk(
        GpuWorkspace workspace,
        FloatReductionKind kind,
        GpuBigFloatArrayView input,
        GpuBigFloatArrayView output,
        (int GridDimX, int BatchSize) pass,
        uint blockDimX)
    {
        var numWords = input.WordsPerElement;
        var chunkCtx = new GpuBigIntVectorKernels.AotContexts.VectorFloatReductionChunk
        {
            Input = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = input.Significands.DevicePointer,
                Exponents = input.Exponents.DevicePointer,
                Signs = input.Signs.DevicePointer,
                NumWords = numWords
            },
            Output = new GpuBigIntVectorKernels.AotContexts.FloatVectorOutput
            {
                Limbs = output.Significands.DevicePointer,
                Exponents = output.Exponents.DevicePointer,
                Signs = output.Signs.DevicePointer,
                NumWords = numWords
            },
            BatchSize = pass.BatchSize,
            ChunkSize = ChunkSize
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
    }
}
