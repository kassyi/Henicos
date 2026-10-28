namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Orchestrates the multi-block Stream Compaction pipeline using Blelloch Scan.
/// </summary>
public static class StreamCompactionOrchestrator
{
    private const int MaxElementsPerBlock = 2048; // Max threads is 1024, each processes 2 elements

    /// <summary>
    /// Executes an exclusive prefix sum (Blelloch Scan) across a potentially large array of elements.
    /// Handles multi-block partitioning recursively.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <param name="outputPtr">The GPU device pointer to receive the exclusive prefix sum result.</param>
    /// <param name="inputPtr">The GPU device pointer holding the input elements.</param>
    /// <param name="elementCount">The number of elements to scan.</param>
    public static void ExecuteExclusiveScan(GpuWorkspace workspace, IntPtr outputPtr, IntPtr inputPtr, int elementCount)
    {
        using var scratch = new ScanScratch(workspace, elementCount);
        ExecuteExclusiveScan(workspace, outputPtr, inputPtr, elementCount, scratch,
            new ScanCount(IntPtr.Zero, 0, elementCount), IntPtr.Zero, flags: false);
    }

    /// <summary>
    /// The number of input elements of a scan, on the device: <c>clamp(Base[0] - Skip, 0, Limit)</c>, or
    /// <c>Limit</c> without a base (design D3 4.1). The elements after it count as 0.
    /// </summary>
    internal readonly record struct ScanCount(IntPtr Base, int Skip, int Limit);

    /// <summary>
    /// The block-sum buffers of every level of a multi-block scan, sized once from the capacity, so that a scan
    /// allocates nothing (a CUDA graph capture cannot allocate). A Where node owns one.
    /// </summary>
    internal sealed class ScanScratch : IDisposable
    {
        private readonly List<(GpuBigUIntArray BlockSums, GpuBigUIntArray Scanned)> _levels = [];

        public ScanScratch(GpuWorkspace workspace, int capacity)
        {
            try
            {
                var count = capacity;
                while (count > MaxElementsPerBlock)
                {
                    var numBlocks = (count + MaxElementsPerBlock - 1) / MaxElementsPerBlock;
                    var blockSums = new GpuBigUIntArray(workspace, numBlocks, 1);
                    try
                    {
                        _levels.Add((blockSums, new GpuBigUIntArray(workspace, numBlocks, 1)));
                    }
                    catch
                    {
                        blockSums.Dispose();
                        throw;
                    }

                    count = numBlocks;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public (GpuBigUIntArray BlockSums, GpuBigUIntArray Scanned) Level(int index) => _levels[index];

        public void Dispose()
        {
            foreach (var (blockSums, scanned) in _levels)
            {
                blockSums.Dispose();
                scanned.Dispose();
            }

            _levels.Clear();
        }
    }

    /// <summary>
    /// Executes an exclusive prefix sum over the first <paramref name="count"/> elements (the rest count as 0), with
    /// the block sums in <paramref name="scratch"/>; writes the sum of all elements into <paramref name="totalPtr"/>
    /// unless it is zero. The total comes from the single-block scan of the last level, so no kernel is added. With
    /// <paramref name="flags"/>, the input are predicates and a non-zero value counts as 1.
    /// </summary>
    internal static void ExecuteExclusiveScan(GpuWorkspace workspace, IntPtr outputPtr, IntPtr inputPtr,
        int elementCount, ScanScratch scratch, ScanCount count, IntPtr totalPtr, bool flags) =>
        ExecuteExclusiveScan(workspace, outputPtr, inputPtr, elementCount, scratch, 0, count, totalPtr, flags);

    private static void ExecuteExclusiveScan(GpuWorkspace workspace, IntPtr outputPtr, IntPtr inputPtr,
        int elementCount, ScanScratch scratch, int level, ScanCount count, IntPtr totalPtr, bool flags)
    {
        if (elementCount == 0)
        {
            if (totalPtr != IntPtr.Zero)
            {
                workspace.Provider.ClearMemoryAsync(totalPtr, 1, workspace.Stream);
            }

            return;
        }

        var numBlocks = (elementCount + MaxElementsPerBlock - 1) / MaxElementsPerBlock;

        if (numBlocks == 1)
        {
            // Single block execution
            var blockDim = (uint)((elementCount + 1) / 2);
            if (blockDim == 0)
            {
                blockDim = 1;
            }

            blockDim = BitOperations.RoundUpToPowerOf2(blockDim);

            GpuStreamCompactionKernels.LaunchBlockExclusiveScanPtr(
                outputPtr, inputPtr, elementCount, IntPtr.Zero, count.Base, count.Skip, count.Limit, totalPtr,
                flags, gridDimX: 1, blockDimX: blockDim, hStream: workspace.Stream);
            return;
        }

        var (blockSums, scannedBlockSums) = scratch.Level(level);
        var fullBlockDim = (uint)(MaxElementsPerBlock / 2);

        // 1. Scan each block individually and record block sums
        GpuStreamCompactionKernels.LaunchBlockExclusiveScanPtr(
            outputPtr, inputPtr, elementCount, blockSums.DevicePointer, count.Base, count.Skip, count.Limit,
            IntPtr.Zero, flags, gridDimX: (uint)numBlocks, blockDimX: fullBlockDim, hStream: workspace.Stream);

        // 2. Recursively scan the block sums (all of them are inputs, and they are counts); its last level writes the
        //    total
        ExecuteExclusiveScan(workspace, scannedBlockSums.DevicePointer, blockSums.DevicePointer, numBlocks, scratch,
            level + 1, new ScanCount(IntPtr.Zero, 0, numBlocks), totalPtr, flags: false);

        // 3. Add the scanned block sums back to the output
        GpuStreamCompactionKernels.LaunchAddBlockSumsPtr(
            outputPtr, scannedBlockSums.DevicePointer, elementCount,
            gridDimX: (uint)numBlocks, blockDimX: 1024, hStream: workspace.Stream); // AddBlockSums uses 1 thread per element, up to 1024 per block
    }

    /// <summary>
    /// Zero-fills the destination and scatters the selected elements of a signed vector (values and signs in one
    /// launch); only the elements before <paramref name="count"/> are read.
    /// </summary>
    internal static void CompactSignedVector(GpuWorkspace workspace, GpuBigIntArrayView output,
        GpuBigIntArrayView input, IntPtr predicatesPtr, IntPtr scanPtr, ScanCount count)
    {
        var elementCount = input.Length;
        if (elementCount == 0)
        {
            return;
        }

        const int threadsPerBlock = 256;
        var totalWords = elementCount * input.WordsPerElement;
        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            output.Values.DevicePointer, totalWords,
            gridDimX: (uint)((totalWords + threadsPerBlock - 1) / threadsPerBlock), blockDimX: threadsPerBlock,
            hStream: workspace.Stream);
        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            output.Signs.DevicePointer, elementCount,
            gridDimX: (uint)((elementCount + threadsPerBlock - 1) / threadsPerBlock), blockDimX: threadsPerBlock,
            hStream: workspace.Stream);
        GpuStreamCompactionKernels.LaunchCompactSignedVectorPtr(
            output.Values.DevicePointer, output.Signs.DevicePointer, input.Values.DevicePointer,
            input.Signs.DevicePointer, predicatesPtr, scanPtr, elementCount, input.WordsPerElement, count.Base,
            count.Skip, count.Limit, gridDimX: (uint)((elementCount + threadsPerBlock - 1) / threadsPerBlock),
            blockDimX: threadsPerBlock, hStream: workspace.Stream);
    }

    /// <summary>
    /// The floating-point form of <see cref="CompactSignedVector"/>: significands, exponents and signs in one launch.
    /// </summary>
    internal static void CompactFloatVector(GpuWorkspace workspace, GpuBigFloatArrayView output,
        GpuBigFloatArrayView input, IntPtr predicatesPtr, IntPtr scanPtr, ScanCount count)
    {
        var elementCount = input.Length;
        if (elementCount == 0)
        {
            return;
        }

        const int threadsPerBlock = 256;
        var totalWords = elementCount * input.WordsPerElement;
        var elementGrid = (uint)((elementCount + threadsPerBlock - 1) / threadsPerBlock);
        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            output.Significands.DevicePointer, totalWords,
            gridDimX: (uint)((totalWords + threadsPerBlock - 1) / threadsPerBlock), blockDimX: threadsPerBlock,
            hStream: workspace.Stream);
        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            output.Exponents.DevicePointer, elementCount, gridDimX: elementGrid, blockDimX: threadsPerBlock,
            hStream: workspace.Stream);
        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            output.Signs.DevicePointer, elementCount, gridDimX: elementGrid, blockDimX: threadsPerBlock,
            hStream: workspace.Stream);
        GpuStreamCompactionKernels.LaunchCompactFloatVectorPtr(
            output.Significands.DevicePointer, output.Exponents.DevicePointer, output.Signs.DevicePointer,
            input.Significands.DevicePointer, input.Exponents.DevicePointer, input.Signs.DevicePointer, predicatesPtr,
            scanPtr, elementCount, input.WordsPerElement, count.Base, count.Skip, count.Limit, gridDimX: elementGrid,
            blockDimX: threadsPerBlock, hStream: workspace.Stream);
    }

    /// <summary>
    /// Encapsulates device pointers and dimensions for a stream compaction scatter operation.
    /// </summary>
    /// <param name="OutputPtr">The destination device pointer.</param>
    /// <param name="InputPtr">The source device pointer.</param>
    /// <param name="PredicatesPtr">The boolean predicates device pointer.</param>
    /// <param name="ScanIndicesPtr">The exclusive scan indices device pointer.</param>
    /// <param name="ElementCount">The number of elements in the source array.</param>
    /// <param name="WordsPerElement">The number of 32-bit words per element.</param>
    public readonly record struct StreamCompactionRequest(
        IntPtr OutputPtr,
        IntPtr InputPtr,
        IntPtr PredicatesPtr,
        IntPtr ScanIndicesPtr,
        int ElementCount,
        int WordsPerElement);

    /// <summary>
    /// Zero-fills a destination buffer and scatters selected elements using precomputed exclusive scan indices.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <param name="request">The stream compaction buffer and dimension parameters.</param>
    public static void CompactArray(GpuWorkspace workspace, StreamCompactionRequest request)
    {
        if (request.ElementCount <= 0 || request.WordsPerElement <= 0)
        {
            return;
        }

        const int scatterThreadsPerBlock = 256;
        var scatterGridDim = (request.ElementCount + scatterThreadsPerBlock - 1) / scatterThreadsPerBlock;
        var totalWords = request.ElementCount * request.WordsPerElement;
        var memsetGridDim = (totalWords + scatterThreadsPerBlock - 1) / scatterThreadsPerBlock;

        GpuStreamCompactionKernels.LaunchMemsetZeroPtr(
            request.OutputPtr, totalWords,
            gridDimX: (uint)memsetGridDim, blockDimX: scatterThreadsPerBlock, hStream: workspace.Stream);

        GpuStreamCompactionKernels.LaunchCompactBigUIntArrayPtr(
            request.OutputPtr, request.InputPtr,
            request.PredicatesPtr, request.ScanIndicesPtr,
            request.ElementCount, request.WordsPerElement,
            gridDimX: (uint)scatterGridDim, blockDimX: scatterThreadsPerBlock, hStream: workspace.Stream);
    }
}
