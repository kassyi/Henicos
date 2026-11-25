// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

// The shared-memory row transform of the cooperative 2-prime NTT: rows in batches, one thread per butterfly.
public static partial class GpuBigIntNttKernels
{
    [GpuDeviceFunction]
    public static void ExecuteNttSharedRow(NumberTheoreticTransform args)
    {
        var modulus = new NttPrimeModulus { P = args.P, Pm = args.Pm };
        int halfLength = args.CurrentL / 2;

        int maxLog2Length = 0;
        while ((1 << maxLog2Length) < args.CurrentL)
        {
            maxLog2Length++;
        }

        // One thread per butterfly: a block of B threads holds B / (L / 2) rows at once (at least one, strided).
        int paddedLength = args.CurrentL + (args.CurrentL >> 5);
        int rowsPerBatch = BlockDimension.X >> (maxLog2Length - 1);
        if (rowsPerBatch < 1)
        {
            rowsPerBatch = 1;
        }

        var sRows = new SharedMemory<uint>(rowsPerBatch * paddedLength);

        // The rows of this block are BlockIndex.X + k * GridDimension.X, k = 0 .. rowsOfBlock - 1.
        int rowsOfBlock = (args.MaxBlocks - BlockIndex.X + GridDimension.X - 1) / GridDimension.X;
        for (int firstSlot = 0; firstSlot < rowsOfBlock; firstSlot += rowsPerBatch)
        {
            int batchRows = rowsOfBlock - firstSlot < rowsPerBatch ? rowsOfBlock - firstSlot : rowsPerBatch;
            int firstRow = BlockIndex.X + firstSlot * GridDimension.X;
            ExecuteNttSharedRowBatch(
                sRows, args, modulus, halfLength, maxLog2Length, paddedLength, firstRow, batchRows);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteNttSharedRowBatch(
        SharedMemory<uint> sRows,
        NumberTheoreticTransform args,
        NttPrimeModulus modulus,
        int halfLength,
        int maxLog2Length,
        int paddedLength,
        int firstRow,
        int batchRows)
    {
        int batchThreads = batchRows * halfLength;
        for (int id = ThreadIndex.X; id < batchThreads; id += BlockDimension.X)
        {
            int slot = id >> (maxLog2Length - 1);
            ExecuteNttLoadAndTwiddleBefore(sRows, slot * paddedLength, args,
                firstRow + slot * GridDimension.X, id & (halfLength - 1), maxLog2Length, modulus);
        }

        Gpu.SyncThreads();

        for (int step = 0; step < maxLog2Length; step++)
        {
            for (int id = ThreadIndex.X; id < batchThreads; id += BlockDimension.X)
            {
                int slot = id >> (maxLog2Length - 1);
                ExecuteNttButterflyStep(sRows, slot * paddedLength, args, step, id & (halfLength - 1), modulus);
            }

            Gpu.SyncThreads();
        }

        for (int id = ThreadIndex.X; id < batchThreads; id += BlockDimension.X)
        {
            int slot = id >> (maxLog2Length - 1);
            ExecuteNttTwiddleAfterAndStore(sRows, slot * paddedLength, args,
                firstRow + slot * GridDimension.X, id & (halfLength - 1), modulus);
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ElementIndex(NumberTheoreticTransform args, int rowIndex, int element) =>
        args.ColumnStride == 0
            ? rowIndex * args.CurrentL + element
            : rowIndex + element * args.ColumnStride;

    [GpuDeviceFunction]
    private static void ExecuteNttLoadAndTwiddleBefore(
        SharedMemory<uint> sRows, int rowBase, NumberTheoreticTransform args,
        int rowIndex, int threadId, int maxLog2Length, NttPrimeModulus modulus)
    {
        int currentLength = args.CurrentL;
        uint value1 = args.Buffer[ElementIndex(args, rowIndex, threadId)];
        uint value2 = args.Buffer[ElementIndex(args, rowIndex, threadId + currentLength / 2)];

        uint reversedThreadId1 = Gpu.BitReverse((uint)threadId) >> (32 - maxLog2Length);
        uint reversedThreadId2 = Gpu.BitReverse((uint)(threadId + currentLength / 2)) >> (32 - maxLog2Length);

        if (args.ApplyTwiddleBefore)
        {
            ulong transformMask = (ulong)(args.Grid.M - 1);
            ulong twiddleIndex1 = ((ulong)rowIndex * (ulong)threadId) & transformMask;
            value1 = ModMul(value1, args.Twiddle[twiddleIndex1], modulus);

            ulong twiddleIndex2 = ((ulong)rowIndex * (ulong)(threadId + (currentLength >> 1))) & transformMask;
            value2 = ModMul(value2, args.Twiddle[twiddleIndex2], modulus);
        }

        sRows[rowBase + (int)(reversedThreadId1 + (reversedThreadId1 >> 5))] = value1;
        sRows[rowBase + (int)(reversedThreadId2 + (reversedThreadId2 >> 5))] = value2;
    }

    [GpuDeviceFunction]
    private static void ExecuteNttButterflyStep(
        SharedMemory<uint> sRows, int rowBase, NumberTheoreticTransform args,
        int step, int threadId, NttPrimeModulus modulus)
    {
        int halfLength = 1 << step;
        int blockIndex = threadId >> step;
        int elementIndex = threadId & (halfLength - 1);
        int position = (blockIndex << (step + 1)) + elementIndex;

        int twiddleFactorIndex = elementIndex * (args.Grid.M >> (step + 1));
        uint twiddleFactor = args.Twiddle[twiddleFactorIndex];

        int padPos = rowBase + position + (position >> 5);
        int posHalf = position + halfLength;
        int padPosHalf = rowBase + posHalf + (posHalf >> 5);

        uint leftTerm = sRows[padPos];
        uint rightTerm = ModMul(sRows[padPosHalf], twiddleFactor, modulus);

        sRows[padPos] = ModAdd(leftTerm, rightTerm, modulus.P);
        sRows[padPosHalf] = ModSub(leftTerm, rightTerm, modulus.P);
    }

    [GpuDeviceFunction]
    private static void ExecuteNttTwiddleAfterAndStore(
        SharedMemory<uint> sRows, int rowBase, NumberTheoreticTransform args,
        int rowIndex, int threadId, NttPrimeModulus modulus)
    {
        int currentLength = args.CurrentL;
        int halfL = currentLength / 2;

        int padId1 = rowBase + threadId + (threadId >> 5);
        int idx2 = threadId + halfL;
        int padId2 = rowBase + idx2 + (idx2 >> 5);

        uint output1 = sRows[padId1];
        uint output2 = sRows[padId2];

        if (args.ApplyTwiddleAfter)
        {
            ulong transformMask = (ulong)(args.Grid.M - 1);
            ulong twiddleIndex1 = ((ulong)rowIndex * (ulong)threadId) & transformMask;
            output1 = ModMul(output1, args.Twiddle[twiddleIndex1], modulus);

            ulong twiddleIndex2 = ((ulong)rowIndex * (ulong)(threadId + (currentLength >> 1))) & transformMask;
            output2 = ModMul(output2, args.Twiddle[twiddleIndex2], modulus);
        }

        args.TemporaryBuffer[ElementIndex(args, rowIndex, threadId)] = output1;
        args.TemporaryBuffer[ElementIndex(args, rowIndex, idx2)] = output2;
    }
}
