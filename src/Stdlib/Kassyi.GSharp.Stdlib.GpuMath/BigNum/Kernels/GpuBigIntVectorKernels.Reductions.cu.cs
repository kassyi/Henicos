#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorSumReductionChunkKernel(VectorReductionChunkContext ctx)
    {
        DeviceVectorSumReductionChunk(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorSumReductionChunk(VectorReductionChunkContext ctx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * ctx.NumWords;

        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = startIndex + ctx.ChunkSize;
        if (endIndex > ctx.BatchSize)
        {
            endIndex = ctx.BatchSize;
        }

        for (int offset = 0; offset < ctx.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < ctx.NumWords)
            {
                ctx.Result[resultOffset + idx] = 0u;
            }
        }

        Gpu.SyncThreads();

        for (int i = startIndex; i < endIndex; i++)
        {
            DeviceVectorAddAccumulateParallel(ctx.A, i * ctx.NumWords, ctx.Result, resultOffset, ctx.NumWords);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddAccumulateParallel(
        [GpuReadOnly] uint[] src,
        int srcOffset,
        uint[] dest,
        int destOffset,
        int numWords)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;

            uint valA = valid ? src[srcOffset + idx] : 0u;
            uint valB = valid ? dest[destOffset + idx] : 0u;

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                dest[destOffset + idx] = (uint)sum + carryIn;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void VectorMaxReductionChunkKernel(VectorReductionChunkContext ctx)
    {
        DeviceVectorCompareReductionChunk(ctx, 2ul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMaxReductionChunk(VectorReductionChunkContext ctx)
    {
        DeviceVectorCompareReductionChunk(ctx, 2ul);
    }

    [GpuKernel]
    public static void VectorMinReductionChunkKernel(VectorReductionChunkContext ctx)
    {
        DeviceVectorCompareReductionChunk(ctx, 1ul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMinReductionChunk(VectorReductionChunkContext ctx)
    {
        DeviceVectorCompareReductionChunk(ctx, 1ul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareReductionChunk(VectorReductionChunkContext ctx, ulong targetCmpBits)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * ctx.NumWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = startIndex + ctx.ChunkSize;
        if (endIndex > ctx.BatchSize)
        {
            endIndex = ctx.BatchSize;
        }

        bool hasElements = startIndex < ctx.BatchSize;
        int firstElemOffset = startIndex * ctx.NumWords;
        for (int offset = 0; offset < ctx.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < ctx.NumWords)
            {
                ctx.Result[resultOffset + idx] = hasElements ? ctx.A[firstElemOffset + idx] : 0u;
            }
        }

        Gpu.SyncThreads();

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            DeviceVectorCompareAccumulateParallel(
                ctx.A, i * ctx.NumWords, ctx.Result, resultOffset, ctx.NumWords, targetCmpBits);
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMaxAccumulateParallel(
        [GpuReadOnly] uint[] src,
        int srcOffset,
        uint[] dest,
        int destOffset,
        int numWords)
    {
        DeviceVectorCompareAccumulateParallel(src, srcOffset, dest, destOffset, numWords, 2ul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMinAccumulateParallel(
        [GpuReadOnly] uint[] src,
        int srcOffset,
        uint[] dest,
        int destOffset,
        int numWords)
    {
        DeviceVectorCompareAccumulateParallel(src, srcOffset, dest, destOffset, numWords, 1ul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareAccumulateParallel(
        [GpuReadOnly] uint[] src,
        int srcOffset,
        uint[] dest,
        int destOffset,
        int numWords,
        ulong targetCmpBits)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        ulong myVal = 0;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint ws = valid ? src[srcOffset + idx] : 0u;
            uint wd = valid ? dest[destOffset + idx] : 0u;

            if (valid && ws != wd)
            {
                long diff = ws - (long)wd;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        uint mask = Warp.ActiveMask();
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(myVal >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)myVal, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > myVal)
            {
                myVal = otherVal;
            }
        }

        if (laneId == 0)
        {
            warpMax[warpId] = myVal;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;

            uint activeMask = Warp.ActiveMask();
            for (uint offset = 16; offset > 0; offset /= 2)
            {
                uint high = Warp.ShuffleDownSync(activeMask, (uint)(val >> 32), offset);
                uint low = Warp.ShuffleDownSync(activeMask, (uint)val, offset);
                ulong otherVal = ((ulong)high << 32) | low;
                if (otherVal > val)
                {
                    val = otherVal;
                }
            }

            if (laneId == 0)
            {
                warpMax[0] = val;
            }
        }

        Gpu.SyncThreads();

        if ((warpMax[0] & 0xFFFFFFFFul) == targetCmpBits)
        {
            for (int offset = 0; offset < numWords; offset += bDim)
            {
                int idx = offset + tid;
                if (idx < numWords)
                {
                    dest[destOffset + idx] = src[srcOffset + idx];
                }
            }
        }

        Gpu.SyncThreads();
    }
}
