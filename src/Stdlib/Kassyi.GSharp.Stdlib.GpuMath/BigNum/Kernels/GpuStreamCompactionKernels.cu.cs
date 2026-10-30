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
    /// The number of elements described by <paramref name="count"/> (design D3 4.1): its limit without a count word,
    /// otherwise <c>clamp(Base[0] - Skip, 0, Limit)</c>.
    /// </summary>
    [GpuDeviceFunction, GpuInline]
    public static int ResolveCount(GpuBigIntVectorKernels.ElementCountContext count)
    {
        if (count.Base == null)
        {
            return count.Limit;
        }

        int n = (int)count.Base[0] - count.Skip;
        return n < 0 ? 0 : (n > count.Limit ? count.Limit : n);
    }

    /// <summary>
    /// Performs a block-wide exclusive prefix sum (Blelloch Scan) on a uint array.
    /// </summary>
    /// <param name="output">The destination array for the exclusive scan result.</param>
    /// <param name="input">The source array.</param>
    /// <param name="elementCount">The number of valid elements to process (must be &lt;= block dimension * 2).</param>
    /// <param name="blockSums">An auxiliary array to store the total sum of this block (used for multi-block scan).</param>
    /// <param name="count">The number of input elements (see <see cref="ResolveCount"/>); the elements after it count
    /// as 0.</param>
    /// <param name="total">Receives the sum of all elements when the launch is a single block, or null.</param>
    /// <param name="flags">The input are the predicates of a Where: a non-zero value counts as 1. The block sums of
    /// the next levels are counts, not flags.</param>
    [GpuKernel]
    public static void BlockExclusiveScan(uint[] output, uint[] input, int elementCount, uint[]? blockSums,
        GpuBigIntVectorKernels.ElementCountContext count, uint[]? total, bool flags)
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

        // Only the elements before the input count take part; a flag counts as 0 or 1
        int n = ResolveCount(count);
        uint valueA = (globalA < elementCount && globalA < n) ? input[globalA] : 0u;
        uint valueB = (globalB < elementCount && globalB < n) ? input[globalB] : 0u;
        sharedMem[indexA + offsetA] = flags ? (valueA != 0u ? 1u : 0u) : valueA;
        sharedMem[indexB + offsetB] = flags ? (valueB != 0u ? 1u : 0u) : valueB;

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

            // A single-block launch is the whole scan (or its last level), so its block sum is the total
            if (total != null && GridDimension.X == 1)
            {
                total[0] = sharedMem[lastIndex + lastOffset];
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

    /// <summary>
    /// Scatters the selected elements of a signed vector (values and signs in one launch) to their scanned positions;
    /// only the elements before the input count are read.
    /// </summary>
    [GpuKernel]
    public static void CompactSignedVector(uint[] outputValues, uint[] outputSigns, uint[] inputValues,
        uint[] inputSigns, uint[] predicates, uint[] scanResult, int elementCount, int wordsPerElement,
        GpuBigIntVectorKernels.ElementCountContext count)
    {
        int globalTid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = ResolveCount(count);
        if (globalTid >= elementCount || globalTid >= n || predicates[globalTid] == 0)
        {
            return;
        }

        int destinationIndex = (int)scanResult[globalTid];
        int srcOffset = globalTid * wordsPerElement;
        int dstOffset = destinationIndex * wordsPerElement;
        for (int i = 0; i < wordsPerElement; i++)
        {
            outputValues[dstOffset + i] = inputValues[srcOffset + i];
        }

        outputSigns[destinationIndex] = inputSigns[globalTid];
    }

    /// <summary>
    /// Scatters the selected elements of a floating-point vector (significands, exponents and signs in one launch);
    /// only the elements before the input count are read.
    /// </summary>
    [GpuKernel]
    public static void CompactFloatVector(uint[] outputSignificands, uint[] outputExponents, uint[] outputSigns,
        uint[] inputSignificands, uint[] inputExponents, uint[] inputSigns, uint[] predicates, uint[] scanResult,
        int elementCount, int wordsPerElement, GpuBigIntVectorKernels.ElementCountContext count)
    {
        int globalTid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int n = ResolveCount(count);
        if (globalTid >= elementCount || globalTid >= n || predicates[globalTid] == 0)
        {
            return;
        }

        int destinationIndex = (int)scanResult[globalTid];
        int srcOffset = globalTid * wordsPerElement;
        int dstOffset = destinationIndex * wordsPerElement;
        for (int i = 0; i < wordsPerElement; i++)
        {
            outputSignificands[dstOffset + i] = inputSignificands[srcOffset + i];
        }

        outputExponents[destinationIndex] = inputExponents[globalTid];
        outputSigns[destinationIndex] = inputSigns[globalTid];
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
