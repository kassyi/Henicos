namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Contains kernels for Stream Compaction operations, including the Work-Efficient Parallel Prefix Sum (Exclusive Scan).
/// This implementation is built from scratch in C# utilizing G#'s zero-allocation device execution paradigm.
/// </summary>
public static partial class GpuStreamCompactionKernels
{
    // The number of memory banks in the shared memory architecture (typically 32).
    private const int LogNumBanks = 5;

    /// <summary>
    /// Calculates the zero-bank-conflict padding offset for a given index.
    /// Inserts a padding word every NUM_BANKS elements to skew memory accesses.
    /// </summary>
    [GpuDeviceFunction, GpuInline]
    private static int GetConflictFreeOffset(int n)
    {
        return (n >> LogNumBanks) + (n >> (2 * LogNumBanks));
    }

    /// <summary>
    /// Performs a block-wide exclusive prefix sum (Blelloch Scan) on a uint array.
    /// </summary>
    /// <param name="output">The destination array for the exclusive scan result.</param>
    /// <param name="input">The source array containing 1s and 0s (or any uints).</param>
    /// <param name="elementCount">The number of valid elements to process (must be &lt;= block dimension * 2).</param>
    /// <param name="blockSums">An auxiliary array to store the total sum of this block (used for multi-block scan).</param>
    [GpuKernel]
    public static void BlockExclusiveScan(uint[] output, uint[] input, int elementCount, uint[] blockSums)
    {
        var sharedMem = new SharedMemory<uint>(2048 + 128);

        int tid = ThreadIndex.X;
        int bIdx = BlockIndex.X;
        int elementsPerBlock = BlockDimension.X * 2;
        int globalOffset = bIdx * elementsPerBlock;

        int treeSize = elementsPerBlock;
        int stride = 1;

        int indexA = tid;
        int indexB = tid + (treeSize / 2);

        int offsetA = GetConflictFreeOffset(indexA);
        int offsetB = GetConflictFreeOffset(indexB);

        int globalA = globalOffset + indexA;
        int globalB = globalOffset + indexB;

        sharedMem[indexA + offsetA] = (globalA < elementCount) ? input[globalA] : 0u;
        sharedMem[indexB + offsetB] = (globalB < elementCount) ? input[globalB] : 0u;

        for (int d = treeSize >> 1; d > 0; d >>= 1)
        {
            Gpu.SyncThreads();

            if (tid < d)
            {
                int ai = stride * (2 * tid + 1) - 1;
                int bi = stride * (2 * tid + 2) - 1;

                ai += GetConflictFreeOffset(ai);
                bi += GetConflictFreeOffset(bi);

                sharedMem[bi] += sharedMem[ai];
            }
            stride *= 2;
        }

        if (tid == 0)
        {
            int lastIndex = treeSize - 1;
            int lastOffset = GetConflictFreeOffset(lastIndex);
            
            if (blockSums != null)
            {
                blockSums[bIdx] = sharedMem[lastIndex + lastOffset];
            }

            sharedMem[lastIndex + lastOffset] = 0u;
        }

        for (int d = 1; d < treeSize; d *= 2)
        {
            stride >>= 1;
            Gpu.SyncThreads();

            if (tid < d)
            {
                int ai = stride * (2 * tid + 1) - 1;
                int bi = stride * (2 * tid + 2) - 1;

                ai += GetConflictFreeOffset(ai);
                bi += GetConflictFreeOffset(bi);

                uint temp = sharedMem[ai];
                sharedMem[ai] = sharedMem[bi];
                sharedMem[bi] += temp;
            }
        }

        Gpu.SyncThreads();

        if (globalA < elementCount)
        {
            output[globalA] = sharedMem[indexA + offsetA];
        }
        if (globalB < elementCount)
        {
            output[globalB] = sharedMem[indexB + offsetB];
        }
    }

    /// <summary>
    /// Adds the scanned block sums to all elements in the corresponding block.
    /// This is Phase 3 of the multi-block arbitrary-size scan.
    /// </summary>
    [GpuKernel]
    public static void AddBlockSums(uint[] output, uint[] blockSums, int elementCount)
    {
        int tid = ThreadIndex.X;
        int bIdx = BlockIndex.X;
        int elementsPerBlock = BlockDimension.X * 2;
        int globalOffset = bIdx * elementsPerBlock;

        int globalA = globalOffset + tid;
        int globalB = globalOffset + tid + (elementsPerBlock / 2);

        uint sum = blockSums[bIdx];

        if (globalA < elementCount)
        {
            output[globalA] += sum;
        }
        if (globalB < elementCount)
        {
            output[globalB] += sum;
        }
    }

    /// <summary>
    /// Scatters the surviving BigUInt elements to the output array based on the predicates and their prefix sums.
    /// This is the final phase of Stream Compaction (Where).
    /// </summary>
    [GpuKernel]
    public static void CompactBigUIntArray(uint[] output, uint[] input, uint[] predicates, uint[] scanResult, int elementCount, int wordsPerElement)
    {
        int globalTid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        if (globalTid >= elementCount)
        {
            return;
        }

        if (predicates[globalTid] == 0)
        {
            return;
        }

        int destinationIndex = (int)scanResult[globalTid];
                
        int srcOffset = globalTid * wordsPerElement;
        int dstOffset = destinationIndex * wordsPerElement;

        for (int i = 0; i < wordsPerElement; i++)
        {
            output[dstOffset + i] = input[srcOffset + i];
        }
    }

    [GpuKernel]
    public static void MemsetZero(uint[] output, int totalWords)
    {
        int globalTid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

        if (globalTid < totalWords)
        {
            output[globalTid] = 0;
        }
    }
}
