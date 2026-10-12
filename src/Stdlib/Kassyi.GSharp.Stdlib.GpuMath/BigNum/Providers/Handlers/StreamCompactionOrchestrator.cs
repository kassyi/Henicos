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
        if (elementCount == 0)
        {
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
                outputPtr, inputPtr, elementCount, IntPtr.Zero,
                gridDimX: 1, blockDimX: blockDim, hStream: workspace.Stream);
            return;
        }

        // Multi-block execution
        // Allocate temporary buffer for block sums
        using var blockSumsBuffer = new GpuBigUIntArray(workspace, numBlocks, 1);

        var fullBlockDim = (uint)(MaxElementsPerBlock / 2);

        // 1. Scan each block individually and record block sums
        GpuStreamCompactionKernels.LaunchBlockExclusiveScanPtr(
            outputPtr, inputPtr, elementCount, blockSumsBuffer.DevicePointer,
            gridDimX: (uint)numBlocks, blockDimX: fullBlockDim, hStream: workspace.Stream);

        // 2. Recursively scan the block sums
        using var scannedBlockSumsBuffer = new GpuBigUIntArray(workspace, numBlocks, 1);
        ExecuteExclusiveScan(workspace, scannedBlockSumsBuffer.DevicePointer, blockSumsBuffer.DevicePointer, numBlocks);

        // 3. Add the scanned block sums back to the output
        GpuStreamCompactionKernels.LaunchAddBlockSumsPtr(
            outputPtr, scannedBlockSumsBuffer.DevicePointer, elementCount,
            gridDimX: (uint)numBlocks, blockDimX: 1024, hStream: workspace.Stream); // AddBlockSums uses 1 thread per element, up to 1024 per block
    }
}
