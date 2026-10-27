#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReduceWarpMaxUInt64(uint mask, ulong val)
    {
        // A block that is not a multiple of 32 threads ends in a partial warp. Lanes past its end do not exist, and a
        // shuffle from one of them returns an undefined value, so only the lanes below validLanes are combined.
        int laneId = ThreadIndex.X % 32;
        int warpStart = ThreadIndex.X - laneId;
        int validLanes = BlockDimension.X - warpStart;
        for (uint offset = 16; offset > 0; offset /= 2)
        {
            uint high = Warp.ShuffleDownSync(mask, (uint)(val >> 32), offset);
            uint low = Warp.ShuffleDownSync(mask, (uint)val, offset);
            ulong otherVal = ((ulong)high << 32) | low;
            if (laneId + (int)offset < validLanes && otherVal > val)
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

        var leftSlice = ctx.Left.Slice;
        var rightSlice = ctx.Right.Slice;
        ulong myVal = 0ul;
        for (int offset = 0; offset < ctx.CompareWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < ctx.CompareWords;
            uint wa = valid ? GetVectorShiftedWord(leftSlice.Data, leftSlice.BaseOffset, leftSlice.NumWords, idx, ctx.Left.Shift) : 0u;
            uint wb = valid ? GetVectorShiftedWord(rightSlice.Data, rightSlice.BaseOffset, rightSlice.NumWords, idx, ctx.Right.Shift) : 0u;

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
        var src = copyCtx.Source;
        for (int offset = 0; offset < src.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < src.NumWords)
            {
                copyCtx.Dest[copyCtx.DestOffset + idx] = src.Data[src.BaseOffset + idx];
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
                    Source = new BlockBitLengthContext
                    {
                        Data = ctx.A,
                        BaseOffset = i * ctx.NumWords,
                        NumWords = ctx.NumWords
                    },
                    Dest = ctx.Result,
                    DestOffset = resultOffset
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
        var src = acc.Source;

        for (int offset = 0; offset < src.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < src.NumWords;

            uint valA = valid ? src.Data[src.BaseOffset + idx] : 0u;
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

    /// <summary>
    /// Converts the accumulator of an integer reduction (two's complement, or offset binary for Max / Min) into the
    /// magnitude in <c>Destination</c> and the sign, overflow and count in <c>Header</c>. Launch one block.
    /// </summary>
    [GpuKernel]
    public static void VectorReductionFinalizeKernel(VectorReductionFinalizeContext ctx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> overflow = new SharedMemory<uint>(1);

        if (ctx.Count == 0)
        {
            for (int offset = 0; offset < ctx.ResultWords; offset += bDim)
            {
                int idx = offset + tid;
                if (idx < ctx.ResultWords)
                {
                    ctx.Destination[idx] = 0u;
                }
            }

            if (tid == 0)
            {
                ctx.Header[0] = 0u;
                ctx.Header[1] = 0u;
            }

            return;
        }

        if (tid == 0)
        {
            overflow[0] = 0u;
        }

        Gpu.SyncThreads();

        uint flipTop = ctx.OffsetBinary ? 0x80000000u : 0u;
        uint top = ctx.Source[ctx.SourceWords - 1] ^ flipTop;
        bool negative = (top & 0x80000000u) != 0u;

        // magnitude = negative ? ~raw + 1 : raw, with the +1 carried through the words of the block.
        uint blockCarryIn = negative ? 1u : 0u;
        for (int offset = 0; offset < ctx.SourceWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < ctx.SourceWords;
            uint raw = valid ? ctx.Source[idx] : 0u;
            if (idx == ctx.SourceWords - 1)
            {
                raw ^= flipTop;
            }

            uint word = negative ? ~raw : raw;
            uint g = 0u;
            uint p = word == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            uint magnitude = word + carryIn;
            if (valid)
            {
                if (idx < ctx.ResultWords)
                {
                    ctx.Destination[idx] = magnitude;
                }
                else if (magnitude != 0u)
                {
                    overflow[0] = 1u;
                }
            }

            Gpu.SyncThreads();
        }

        if (tid == 0)
        {
            // A negative accumulator never has a zero magnitude, so "negative" needs no separate zero test.
            ctx.Header[0] = (negative ? 1u : 0u) | (overflow[0] != 0u ? 2u : 0u);
            ctx.Header[1] = (uint)ctx.Count;
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
                    Source = new BlockBitLengthContext
                    {
                        Data = ctx.A,
                        BaseOffset = i * ctx.NumWords,
                        NumWords = ctx.NumWords
                    },
                    Dest = ctx.Result,
                    DestOffset = resultOffset,
                    TargetCmpBits = targetCmpBits
                });
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorCompareAccumulateParallel(VectorAccumulateContext acc)
    {
        SharedMemory<ulong> warpMax = new SharedMemory<ulong>(32);
        var src = acc.Source;

        uint cmpBits = CompareShiftedLimbsBlockParallel(
            new ShiftedLimbCompareContext
            {
                Left = new ShiftedLimbSliceContext
                {
                    Slice = src,
                    Shift = 0
                },
                Right = new ShiftedLimbSliceContext
                {
                    Slice = new BlockBitLengthContext
                    {
                        Data = acc.Dest,
                        BaseOffset = acc.DestOffset,
                        NumWords = src.NumWords
                    },
                    Shift = 0
                },
                CompareWords = src.NumWords
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

        uint bitLenA = ComputeBlockBitLength(cmp.Left.Slice, warpMax);
        uint bitLenB = ComputeBlockBitLength(cmp.Right.Slice, warpMax);

        if (bitLenA == 0u && bitLenB == 0u)
        {
            return 0u;
        }

        if (bitLenA == 0u)
        {
            return cmp.Right.Sign != 0u ? 2u : 1u;
        }

        if (bitLenB == 0u)
        {
            return cmp.Left.Sign != 0u ? 1u : 2u;
        }

        if (cmp.Left.Sign != cmp.Right.Sign)
        {
            return cmp.Left.Sign != 0u ? 1u : 2u;
        }

        long scaleA = cmp.Left.Exp + bitLenA;
        long scaleB = cmp.Right.Exp + bitLenB;
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
                    Left = new ShiftedLimbSliceContext
                    {
                        Slice = cmp.Left.Slice,
                        Shift = maxBits - (int)bitLenA
                    },
                    Right = new ShiftedLimbSliceContext
                    {
                        Slice = cmp.Right.Slice,
                        Shift = maxBits - (int)bitLenB
                    },
                    CompareWords = (maxBits + 31) / 32
                },
                warpMax);
        }

        if (magCmp == 0u)
        {
            return 0u;
        }

        return cmp.Left.Sign != 0u ? (3u - magCmp) : magCmp;
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
                Left = new FloatElementSliceContext
                {
                    Slice = new BlockBitLengthContext
                    {
                        Data = ctx.Left.Limbs,
                        BaseOffset = batchIdx * ctx.Left.NumWords,
                        NumWords = ctx.Left.NumWords
                    },
                    Exp = (int)ctx.Left.Exponents[batchIdx],
                    Sign = ctx.Left.Signs[batchIdx]
                },
                Right = new FloatElementSliceContext
                {
                    Slice = new BlockBitLengthContext
                    {
                        Data = ctx.Right.Limbs,
                        BaseOffset = (ctx.IsScalar ? 0 : batchIdx) * ctx.Right.NumWords,
                        NumWords = ctx.Right.NumWords
                    },
                    Exp = ctx.IsScalar ? ctx.ScalarExp : (int)ctx.Right.Exponents[batchIdx],
                    Sign = ctx.IsScalar ? ctx.ScalarSign : ctx.Right.Signs[batchIdx]
                }
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
        int numWords = ctx.Input.NumWords;
        int resultOffset = bIdx * numWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = startIndex + ctx.ChunkSize;
        if (endIndex > ctx.BatchSize)
        {
            endIndex = ctx.BatchSize;
        }

        bool hasElements = startIndex < ctx.BatchSize;
        int firstElemOffset = startIndex * numWords;
        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < numWords)
            {
                ctx.Output.Limbs[resultOffset + idx] = hasElements ? ctx.Input.Limbs[firstElemOffset + idx] : 0u;
            }
        }

        if (tid == 0)
        {
            ctx.Output.Exponents[bIdx] = hasElements ? ctx.Input.Exponents[startIndex] : 0u;
            ctx.Output.Signs[bIdx] = hasElements ? ctx.Input.Signs[startIndex] : 0u;
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
        int numWords = ctx.Input.NumWords;
        int resultOffset = bIdx * numWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = InitFloatReductionChunkElement(ctx);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            int srcOffset = i * numWords;
            int srcExp = (int)ctx.Input.Exponents[i];
            uint srcSign = ctx.Input.Signs[i];
            var srcSlice = new BlockBitLengthContext
            {
                Data = ctx.Input.Limbs,
                BaseOffset = srcOffset,
                NumWords = numWords
            };

            uint cmpRes = DeviceVectorFloatCompareElement(
                new ElementCompareContext
                {
                    Left = new FloatElementSliceContext
                    {
                        Slice = srcSlice,
                        Exp = srcExp,
                        Sign = srcSign
                    },
                    Right = new FloatElementSliceContext
                    {
                        Slice = new BlockBitLengthContext
                        {
                            Data = ctx.Output.Limbs,
                            BaseOffset = resultOffset,
                            NumWords = numWords
                        },
                        Exp = (int)ctx.Output.Exponents[bIdx],
                        Sign = ctx.Output.Signs[bIdx]
                    }
                });

            if (cmpRes == targetCmpBits)
            {
                CopyElementWordsBlockParallel(
                    new VectorAccumulateContext
                    {
                        Source = srcSlice,
                        Dest = ctx.Output.Limbs,
                        DestOffset = resultOffset
                    });

                if (tid == 0)
                {
                    ctx.Output.Exponents[bIdx] = (uint)srcExp;
                    ctx.Output.Signs[bIdx] = srcSign;
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

        var srcSlice = step.Source.Slice;
        int numWords = srcSlice.NumWords;
        int eA = (int)step.ExpResult[step.BlockIdx];
        uint signA = step.SignResult[step.BlockIdx];

        uint bitLenA = ComputeBlockBitLength(
            new BlockBitLengthContext
            {
                Data = step.Dest,
                BaseOffset = step.DestOffset,
                NumWords = numWords
            },
            sharedMax);

        uint bitLenB = ComputeBlockBitLength(srcSlice, sharedMax);

        if (bitLenB == 0u)
        {
            return;
        }

        if (bitLenA == 0u)
        {
            CopyElementWordsBlockParallel(
                new VectorAccumulateContext
                {
                    Source = srcSlice,
                    Dest = step.Dest,
                    DestOffset = step.DestOffset
                });

            if (tid == 0)
            {
                step.ExpResult[step.BlockIdx] = (uint)step.Source.Exp;
                step.SignResult[step.BlockIdx] = step.Source.Sign;
            }

            Gpu.SyncThreads();
            return;
        }

        var align = new FloatAlignmentInput
        {
            ExpA = eA,
            ExpB = step.Source.Exp,
            BitLenA = bitLenA,
            BitLenB = bitLenB,
            NumWords = numWords
        };
        ComputeFloatAlignmentOffsets(ref align);

        ShiftElementInPlaceBlockParallel(
            new MutableElementSliceContext
            {
                Buffer = step.Dest,
                BaseOffset = step.DestOffset,
                NumWords = numWords,
                BitOffset = align.BitOffsetA
            });

        bool differentSigns = signA != step.Source.Sign;
        uint blockCarryIn = differentSigns ? 1u : 0u;

        for (int offset = 0; offset < numWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < numWords;
            uint valA = valid ? step.Dest[step.DestOffset + idx] : 0u;
            uint valB = valid ? GetVectorShiftedWord(srcSlice.Data, srcSlice.BaseOffset, numWords, idx, align.BitOffsetB) : 0u;

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
                uint topWord = step.Dest[step.DestOffset + numWords - 1];
                bool isNegTc = (topWord & 0x80000000u) != 0u;
                step.SignResult[step.BlockIdx] = isNegTc ? step.Source.Sign : signA;
                needsNegate[0] = isNegTc ? 1u : 0u;
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
                    NumWords = numWords,
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
        int numWords = ctx.Input.NumWords;
        int resultOffset = bIdx * numWords;
        int startIndex = bIdx * ctx.ChunkSize;
        int endIndex = InitFloatReductionChunkElement(ctx);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            AccumulateFloatElementBlockParallel(
                new FloatAccumulateStepContext
                {
                    Source = new FloatElementSliceContext
                    {
                        Slice = new BlockBitLengthContext
                        {
                            Data = ctx.Input.Limbs,
                            BaseOffset = i * numWords,
                            NumWords = numWords
                        },
                        Exp = (int)ctx.Input.Exponents[i],
                        Sign = ctx.Input.Signs[i]
                    },
                    Dest = ctx.Output.Limbs,
                    DestOffset = resultOffset,
                    ExpResult = ctx.Output.Exponents,
                    SignResult = ctx.Output.Signs,
                    BlockIdx = bIdx
                });
        }
    }
}
