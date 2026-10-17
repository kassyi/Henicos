#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    public struct VectorAccumulateContext
    {
        [GpuReadOnly]
        public uint[] Src;

        public int SrcOffset;
        public uint[] Dest;
        public int DestOffset;
        public int NumWords;
        public ulong TargetCmpBits;
    }

    public struct BlockBitLengthContext
    {
        [GpuReadOnly]
        public uint[] Data;

        public int BaseOffset;
        public int NumWords;
    }

    public struct ElementCompareContext
    {
        [GpuReadOnly]
        public uint[] A;

        public int BaseA;
        public int NumWordsA;
        public int ExpA;
        public uint SignA;

        [GpuReadOnly]
        public uint[] B;

        public int BaseB;
        public int NumWordsB;
        public int ExpB;
        public uint SignB;
    }

    public struct ShiftedLimbCompareContext
    {
        [GpuReadOnly]
        public uint[] A;

        public int BaseA;
        public int NumWordsA;
        public int ShiftA;

        [GpuReadOnly]
        public uint[] B;

        public int BaseB;
        public int NumWordsB;
        public int ShiftB;
        public int CompareWords;
    }

    public struct FloatAlignmentInput
    {
        public int ExpA;
        public int ExpB;
        public uint BitLenA;
        public uint BitLenB;
        public int NumWords;
        public int BitOffsetA;
        public int BitOffsetB;
        public int OutExp;
    }

    public struct MutableElementSliceContext
    {
        public uint[] Buffer;
        public int BaseOffset;
        public int NumWords;
        public int BitOffset;
    }

    public struct FloatAccumulateStepContext
    {
        [GpuReadOnly]
        public uint[] Src;

        public int SrcOffset;
        public int SrcExp;
        public uint SrcSign;
        public uint[] Dest;
        public int DestOffset;
        public uint[] ExpResult;
        public uint[] SignResult;
        public int BlockIdx;
        public int NumWords;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReduceWarpMaxUInt64(uint mask, ulong val)
    {
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(val >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)val, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (otherVal > val)
            {
                val = otherVal;
            }
        }

        return val;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReduceBlockMaxUInt64(ulong myVal, SharedMemory<ulong> warpMax)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        myVal = ReduceWarpMaxUInt64(Warp.ActiveMask(), myVal);
        if (laneId == 0)
        {
            warpMax[warpId] = myVal;
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            ulong val = laneId < numWarps ? warpMax[laneId] : 0ul;
            val = ReduceWarpMaxUInt64(Warp.ActiveMask(), val);
            if (laneId == 0)
            {
                warpMax[0] = val;
            }
        }

        Gpu.SyncThreads();
        ulong result = warpMax[0];
        Gpu.SyncThreads();
        return result;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint CompareShiftedLimbsBlockParallel(ShiftedLimbCompareContext ctx, SharedMemory<ulong> warpMax)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;

        ulong myVal = 0ul;
        for (int offset = 0; offset < ctx.CompareWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < ctx.CompareWords;
            uint wa = valid ? GetVectorShiftedWord(ctx.A, ctx.BaseA, ctx.NumWordsA, idx, ctx.ShiftA) : 0u;
            uint wb = valid ? GetVectorShiftedWord(ctx.B, ctx.BaseB, ctx.NumWordsB, idx, ctx.ShiftB) : 0u;

            if (valid && wa != wb)
            {
                long diff = wa - (long)wb;
                ulong signMask = (ulong)(diff >> 63);
                ulong resultBits = 2ul - (signMask & 1ul);
                myVal = ((ulong)idx << 32) | resultBits;
            }
        }

        ulong maxPacked = ReduceBlockMaxUInt64(myVal, warpMax);
        return (uint)(maxPacked & 0xFFFFFFFFul);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyElementWordsBlockParallel(VectorAccumulateContext copyCtx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        for (int offset = 0; offset < copyCtx.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < copyCtx.NumWords)
            {
                copyCtx.Dest[copyCtx.DestOffset + idx] = copyCtx.Src[copyCtx.SrcOffset + idx];
            }
        }
    }

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
            DeviceVectorAddAccumulateParallel(
                new VectorAccumulateContext
                {
                    Src = ctx.A,
                    SrcOffset = i * ctx.NumWords,
                    Dest = ctx.Result,
                    DestOffset = resultOffset,
                    NumWords = ctx.NumWords
                });
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddAccumulateParallel(VectorAccumulateContext acc)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = 0u;

        for (int offset = 0; offset < acc.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < acc.NumWords;

            uint valA = valid ? acc.Src[acc.SrcOffset + idx] : 0u;
            uint valB = valid ? acc.Dest[acc.DestOffset + idx] : 0u;

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                acc.Dest[acc.DestOffset + idx] = (uint)sum + carryIn;
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
                new VectorAccumulateContext
                {
                    Src = ctx.A,
                    SrcOffset = i * ctx.NumWords,
                    Dest = ctx.Result,
                    DestOffset = resultOffset,
                    NumWords = ctx.NumWords,
                    TargetCmpBits = targetCmpBits
                });
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareAccumulateParallel(VectorAccumulateContext acc)
    {
        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        uint cmpBits = CompareShiftedLimbsBlockParallel(
            new ShiftedLimbCompareContext
            {
                A = acc.Src,
                BaseA = acc.SrcOffset,
                NumWordsA = acc.NumWords,
                ShiftA = 0,
                B = acc.Dest,
                BaseB = acc.DestOffset,
                NumWordsB = acc.NumWords,
                ShiftB = 0,
                CompareWords = acc.NumWords
            },
            warpMax);

        if (cmpBits == (uint)acc.TargetCmpBits)
        {
            CopyElementWordsBlockParallel(acc);
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ComputeBlockBitLength(BlockBitLengthContext ctx, SharedMemory<ulong> sharedMax)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        uint myBits = 0u;
        for (int offset = 0; offset < ctx.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < ctx.NumWords)
            {
                uint w = ctx.Data[ctx.BaseOffset + idx];
                if (w != 0u)
                {
                    uint bitLen = 0u;
                    while (w > 0u)
                    {
                        bitLen++;
                        w >>= 1;
                    }

                    uint cand = (uint)(idx * 32) + bitLen;
                    if (cand > myBits)
                    {
                        myBits = cand;
                    }
                }
            }
        }

        return (uint)ReduceBlockMaxUInt64(myBits, sharedMax);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint DeviceVectorFloatCompareElement(ElementCompareContext cmp)
    {
        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);

        uint bitLenA = ComputeBlockBitLength(
            new BlockBitLengthContext
            {
                Data = cmp.A,
                BaseOffset = cmp.BaseA,
                NumWords = cmp.NumWordsA
            },
            warpMax);

        uint bitLenB = ComputeBlockBitLength(
            new BlockBitLengthContext
            {
                Data = cmp.B,
                BaseOffset = cmp.BaseB,
                NumWords = cmp.NumWordsB
            },
            warpMax);

        if (bitLenA == 0u && bitLenB == 0u)
        {
            return 0u;
        }

        if (bitLenA == 0u)
        {
            return cmp.SignB != 0u ? 2u : 1u;
        }

        if (bitLenB == 0u)
        {
            return cmp.SignA != 0u ? 1u : 2u;
        }

        if (cmp.SignA != cmp.SignB)
        {
            return cmp.SignA != 0u ? 1u : 2u;
        }

        long scaleA = cmp.ExpA + bitLenA;
        long scaleB = cmp.ExpB + bitLenB;
        uint magCmp;

        if (scaleA != scaleB)
        {
            magCmp = scaleA > scaleB ? 2u : 1u;
        }
        else
        {
            int maxBits = (int)(bitLenA > bitLenB ? bitLenA : bitLenB);
            magCmp = CompareShiftedLimbsBlockParallel(
                new ShiftedLimbCompareContext
                {
                    A = cmp.A,
                    BaseA = cmp.BaseA,
                    NumWordsA = cmp.NumWordsA,
                    ShiftA = maxBits - (int)bitLenA,
                    B = cmp.B,
                    BaseB = cmp.BaseB,
                    NumWordsB = cmp.NumWordsB,
                    ShiftB = maxBits - (int)bitLenB,
                    CompareWords = (maxBits + 31) / 32
                },
                warpMax);
        }

        if (magCmp == 0u)
        {
            return 0u;
        }

        return cmp.SignA != 0u ? (3u - magCmp) : magCmp;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EvaluateCompareOperator(uint cmpRes, int opType)
    {
        switch (opType)
        {
            case 0: return cmpRes == 0u;
            case 1: return cmpRes != 0u;
            case 2: return cmpRes == 2u;
            case 3: return cmpRes == 2u || cmpRes == 0u;
            case 4: return cmpRes == 1u;
            case 5: return cmpRes == 1u || cmpRes == 0u;
            default: return false;
        }
    }

    [GpuKernel]
    public static void VectorFloatCompareKernel(VectorFloatCompareContext ctx)
    {
        int tid = ThreadIndex.X;
        int batchIdx = BlockIndex.X;

        uint cmpRes = DeviceVectorFloatCompareElement(
            new ElementCompareContext
            {
                A = ctx.A,
                BaseA = batchIdx * ctx.NumWordsA,
                NumWordsA = ctx.NumWordsA,
                ExpA = (int)ctx.ExpA[batchIdx],
                SignA = ctx.SignA[batchIdx],
                B = ctx.B,
                BaseB = (ctx.IsScalar ? 0 : batchIdx) * ctx.NumWordsB,
                NumWordsB = ctx.NumWordsB,
                ExpB = ctx.IsScalar ? ctx.ScalarExp : (int)ctx.ExpB[batchIdx],
                SignB = ctx.IsScalar ? ctx.ScalarSign : ctx.SignB[batchIdx]
            });

        if (tid == 0)
        {
            ctx.Result[batchIdx] = EvaluateCompareOperator(cmpRes, ctx.OpType) ? 1u : 0u;
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitFloatReductionChunkElement(VectorFloatReductionChunkContext ctx)
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

        if (tid == 0)
        {
            ctx.ExpResult[bIdx] = hasElements ? ctx.ExpA[startIndex] : 0u;
            ctx.SignResult[bIdx] = hasElements ? ctx.SignA[startIndex] : 0u;
        }

        Gpu.SyncThreads();
        return endIndex;
    }

    [GpuKernel]
    public static void VectorFloatMaxReductionChunkKernel(VectorFloatReductionChunkContext ctx)
    {
        DeviceVectorFloatCompareReductionChunk(ctx, 2u);
    }

    [GpuKernel]
    public static void VectorFloatMinReductionChunkKernel(VectorFloatReductionChunkContext ctx)
    {
        DeviceVectorFloatCompareReductionChunk(ctx, 1u);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatCompareReductionChunk(VectorFloatReductionChunkContext ctx, uint targetCmpBits)
    {
        int tid = ThreadIndex.X;
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * ctx.NumWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = InitFloatReductionChunkElement(ctx);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            int srcOffset = i * ctx.NumWords;
            int srcExp = (int)ctx.ExpA[i];
            uint srcSign = ctx.SignA[i];

            uint cmpRes = DeviceVectorFloatCompareElement(
                new ElementCompareContext
                {
                    A = ctx.A,
                    BaseA = srcOffset,
                    NumWordsA = ctx.NumWords,
                    ExpA = srcExp,
                    SignA = srcSign,
                    B = ctx.Result,
                    BaseB = resultOffset,
                    NumWordsB = ctx.NumWords,
                    ExpB = (int)ctx.ExpResult[bIdx],
                    SignB = ctx.SignResult[bIdx]
                });

            if (cmpRes == targetCmpBits)
            {
                CopyElementWordsBlockParallel(
                    new VectorAccumulateContext
                    {
                        Src = ctx.A,
                        SrcOffset = srcOffset,
                        Dest = ctx.Result,
                        DestOffset = resultOffset,
                        NumWords = ctx.NumWords
                    });

                if (tid == 0)
                {
                    ctx.ExpResult[bIdx] = (uint)srcExp;
                    ctx.SignResult[bIdx] = srcSign;
                }
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ComputeFloatAlignmentOffsets(ref FloatAlignmentInput align)
    {
        int bitOffsetA = 0;
        int bitOffsetB = 0;
        int outExp = align.ExpA;
        int totalCapBits = (align.NumWords * 32) - 2;
        if (totalCapBits < 0)
        {
            totalCapBits = 0;
        }

        if (align.ExpA > align.ExpB)
        {
            int diff = align.ExpA - align.ExpB;
            int headroomA = totalCapBits - (int)align.BitLenA;
            if (headroomA < 0)
            {
                headroomA = 0;
            }

            if (diff <= headroomA)
            {
                bitOffsetA = diff;
                outExp = align.ExpB;
            }
            else
            {
                bitOffsetA = headroomA;
                bitOffsetB = headroomA - diff;
                outExp = align.ExpA - headroomA;
            }
        }
        else if (align.ExpB > align.ExpA)
        {
            int diff = align.ExpB - align.ExpA;
            int headroomB = totalCapBits - (int)align.BitLenB;
            if (headroomB < 0)
            {
                headroomB = 0;
            }

            if (diff <= headroomB)
            {
                bitOffsetB = diff;
                outExp = align.ExpA;
            }
            else
            {
                bitOffsetB = headroomB;
                bitOffsetA = headroomB - diff;
                outExp = align.ExpB - headroomB;
            }
        }

        align.BitOffsetA = bitOffsetA;
        align.BitOffsetB = bitOffsetB;
        align.OutExp = outExp;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ShiftElementInPlaceBlockParallel(MutableElementSliceContext slice)
    {
        if (slice.BitOffset == 0)
        {
            return;
        }

        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;

        if (slice.BitOffset > 0)
        {
            int lastTile = ((slice.NumWords - 1) / bDim) * bDim;
            for (int offset = lastTile; offset >= 0; offset -= bDim)
            {
                int idx = offset + tid;
                bool valid = idx < slice.NumWords;
                uint shifted = valid ? GetVectorShiftedWord(slice.Buffer, slice.BaseOffset, slice.NumWords, idx, slice.BitOffset) : 0u;
                Gpu.SyncThreads();
                if (valid)
                {
                    slice.Buffer[slice.BaseOffset + idx] = shifted;
                }

                Gpu.SyncThreads();
            }
        }
        else
        {
            for (int offset = 0; offset < slice.NumWords; offset += bDim)
            {
                int idx = offset + tid;
                bool valid = idx < slice.NumWords;
                uint shifted = valid ? GetVectorShiftedWord(slice.Buffer, slice.BaseOffset, slice.NumWords, idx, slice.BitOffset) : 0u;
                Gpu.SyncThreads();
                if (valid)
                {
                    slice.Buffer[slice.BaseOffset + idx] = shifted;
                }

                Gpu.SyncThreads();
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NegateElementInPlaceBlockParallel(MutableElementSliceContext slice)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = 1u;
        for (int offset = 0; offset < slice.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < slice.NumWords;
            uint valA = valid ? ~slice.Buffer[slice.BaseOffset + idx] : 0u;

            uint g = 0u;
            uint p = valA == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                slice.Buffer[slice.BaseOffset + idx] = valA + carryIn;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AccumulateFloatElementBlockParallel(FloatAccumulateStepContext step)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<ulong> sharedMax = new SharedMemory<ulong>(32);
        SharedMemory<uint> needsNegate = new SharedMemory<uint>(1);

        int eA = (int)step.ExpResult[step.BlockIdx];
        uint signA = step.SignResult[step.BlockIdx];

        uint bitLenA = ComputeBlockBitLength(
            new BlockBitLengthContext
            {
                Data = step.Dest,
                BaseOffset = step.DestOffset,
                NumWords = step.NumWords
            },
            sharedMax);

        uint bitLenB = ComputeBlockBitLength(
            new BlockBitLengthContext
            {
                Data = step.Src,
                BaseOffset = step.SrcOffset,
                NumWords = step.NumWords
            },
            sharedMax);

        if (bitLenB == 0u)
        {
            return;
        }

        if (bitLenA == 0u)
        {
            CopyElementWordsBlockParallel(
                new VectorAccumulateContext
                {
                    Src = step.Src,
                    SrcOffset = step.SrcOffset,
                    Dest = step.Dest,
                    DestOffset = step.DestOffset,
                    NumWords = step.NumWords
                });

            if (tid == 0)
            {
                step.ExpResult[step.BlockIdx] = (uint)step.SrcExp;
                step.SignResult[step.BlockIdx] = step.SrcSign;
            }

            Gpu.SyncThreads();
            return;
        }

        var align = new FloatAlignmentInput
        {
            ExpA = eA,
            ExpB = step.SrcExp,
            BitLenA = bitLenA,
            BitLenB = bitLenB,
            NumWords = step.NumWords
        };
        ComputeFloatAlignmentOffsets(ref align);

        ShiftElementInPlaceBlockParallel(
            new MutableElementSliceContext
            {
                Buffer = step.Dest,
                BaseOffset = step.DestOffset,
                NumWords = step.NumWords,
                BitOffset = align.BitOffsetA
            });

        bool differentSigns = signA != step.SrcSign;
        uint blockCarryIn = differentSigns ? 1u : 0u;

        for (int offset = 0; offset < step.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < step.NumWords;
            uint valA = valid ? step.Dest[step.DestOffset + idx] : 0u;
            uint valB = valid ? GetVectorShiftedWord(step.Src, step.SrcOffset, step.NumWords, idx, align.BitOffsetB) : 0u;

            if (differentSigns)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                step.Dest[step.DestOffset + idx] = (uint)sum + carryIn;
            }

            Gpu.SyncThreads();
        }

        if (tid == 0)
        {
            step.ExpResult[step.BlockIdx] = (uint)align.OutExp;
            if (!differentSigns)
            {
                step.SignResult[step.BlockIdx] = signA;
                needsNegate[0] = 0u;
            }
            else
            {
                uint topWord = step.Dest[step.DestOffset + step.NumWords - 1];
                bool isNegTC = (topWord & 0x80000000u) != 0u;
                step.SignResult[step.BlockIdx] = isNegTC ? step.SrcSign : signA;
                needsNegate[0] = isNegTC ? 1u : 0u;
            }
        }

        Gpu.SyncThreads();

        if (needsNegate[0] == 1u)
        {
            NegateElementInPlaceBlockParallel(
                new MutableElementSliceContext
                {
                    Buffer = step.Dest,
                    BaseOffset = step.DestOffset,
                    NumWords = step.NumWords,
                    BitOffset = 0
                });
        }
    }

    [GpuKernel]
    public static void VectorFloatSumReductionChunkKernel(VectorFloatReductionChunkContext ctx)
    {
        DeviceVectorFloatSumReductionChunk(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatSumReductionChunk(VectorFloatReductionChunkContext ctx)
    {
        int bIdx = BlockIndex.X;
        int resultOffset = bIdx * ctx.NumWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = InitFloatReductionChunkElement(ctx);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            AccumulateFloatElementBlockParallel(
                new FloatAccumulateStepContext
                {
                    Src = ctx.A,
                    SrcOffset = i * ctx.NumWords,
                    SrcExp = (int)ctx.ExpA[i],
                    SrcSign = ctx.SignA[i],
                    Dest = ctx.Result,
                    DestOffset = resultOffset,
                    ExpResult = ctx.ExpResult,
                    SignResult = ctx.SignResult,
                    BlockIdx = bIdx,
                    NumWords = ctx.NumWords
                });
        }
    }
}
