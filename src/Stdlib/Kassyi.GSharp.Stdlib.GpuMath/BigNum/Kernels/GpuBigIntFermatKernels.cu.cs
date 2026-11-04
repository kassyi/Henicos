namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntFermatKernels
{
    /// <summary>
    /// The buffers and sizes of one multi-block parallel fold pass (<see cref="QuadraticFoldPassBlockedKernel"/>).
    /// </summary>
    public struct QuadraticFoldBlockedPassContext
    {
        /// <summary>The input words of this fold pass, <see cref="SourceWords"/> words.</summary>
        /// <remarks>Not <c>[GpuReadOnly]</c>: passed to <see cref="GetQuotientWord"/>.</remarks>
        public uint[] Source;

        /// <summary>Receives the folded output modulo <c>2^(32 * ResultWords)</c>, <see cref="ResultWords"/> words.</summary>
        public uint[] Result;

        /// <summary>Receives one outgoing carry (<c>0..4</c>) per chunk of <see cref="Result"/>.</summary>
        public uint[] BlockCarries;

        /// <summary>The word count of <see cref="Source"/>.</summary>
        public int SourceWords;

        /// <summary>The word count of <see cref="Result"/>.</summary>
        public int ResultWords;

        /// <summary>The fold position <c>m = 2E + 1</c>.</summary>
        public int ModulusBits;

        /// <summary>The exponent <c>E</c>.</summary>
        public int Exponent;

        /// <summary>The constant term <c>t</c> of <c>c = 2^E - t</c> (11).</summary>
        public uint ConstantTerm;
    }

    /// <summary>
    /// The buffers and sizes of the cross-chunk carry application pass (<see cref="QuadraticFoldPassApplyKernel"/>).
    /// </summary>
    public struct QuadraticFoldBlockedApplyContext
    {
        /// <summary>The folded output buffer whose chunk heads receive outgoing carries, <see cref="ResultWords"/> words.</summary>
        public uint[] Result;

        /// <summary>The per-chunk carries written by <see cref="QuadraticFoldPassBlockedKernel"/>.</summary>
        public uint[] BlockCarries;

        /// <summary>The word count of <see cref="Result"/>.</summary>
        public int ResultWords;

        /// <summary>The word count of one chunk (the block size of <see cref="QuadraticFoldPassBlockedKernel"/>).</summary>
        public int ChunkSize;

        /// <summary>The number of chunks resolved by <see cref="QuadraticFoldPassBlockedKernel"/>.</summary>
        public int NumBlocks;
    }

    /// <summary>Word and bit offsets derived from <c>m = 2E + 1</c> and <c>E</c> for a fold pass.</summary>
    public struct QuadraticFoldSliceOffsets
    {
        public int FoldWord;
        public int FoldBit;
        public int ShiftWord;
        public int ShiftBit;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16(
        GpuBigIntMulKernels.NttUnpackOperandContext operand, int n, bool syncThreads = true)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint val = 0u;
            int wordIdx = i >> 1;
            if (wordIdx < operand.NumWords)
            {
                uint w = operand.Source[wordIdx];
                val = (i & 1) == 0 ? (w & 0xFFFFu) : (w >> 16);
            }

            operand.NttBuf1[i] = val;
            operand.NttBuf2[i] = val;
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords,
        bool syncThreads = true)
    {
        DeviceUnpack16(
            new GpuBigIntMulKernels.NttUnpackOperandContext
            {
                Source = dSrc,
                NttBuf1 = dDst1,
                NttBuf2 = dDst2,
                NumWords = numWords
            },
            n,
            syncThreads);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16Transposed(
        GpuBigIntMulKernels.NttUnpackOperandContext operand, NttGridContext grid, bool syncThreads = true)
    {
        int log2N2 = 0;
        if (grid.M > 512)
        {
            while ((1 << log2N2) < grid.N2)
            {
                log2N2++;
            }
        }

        int n2Mask = grid.N2 - 1;
        Gpu.GridStrideLoop(grid.M, i =>
        {
            uint val = 0u;
            int wordIdx = i >> 1;
            if (wordIdx < operand.NumWords)
            {
                uint w = operand.Source[wordIdx];
                val = (i & 1) == 0 ? (w & 0xFFFFu) : (w >> 16);
            }

            int dstIdx = grid.M > 512 ? ((i & n2Mask) * grid.N1 + (i >> log2N2)) : i;
            operand.NttBuf1[dstIdx] = val;
            operand.NttBuf2[dstIdx] = val;
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceTranspose32X32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height,
        bool syncThreads = true)
    {
        var sMem = new SharedMemory<uint>(32 * 33);

        var gridX = (width + 31) / 32;
        var gridY = (height + 31) / 32;
        var totalBlocks = gridX * gridY;

        for (int bIdx = BlockIndex.X; bIdx < totalBlocks; bIdx += GridDimension.X)
        {
            var bx = bIdx % gridX;
            var by = bIdx / gridX;

            if (ThreadIndex.X < 256)
            {
                var tx = ThreadIndex.X % 32;
                var ty = ThreadIndex.X / 32;

                var xIndex = bx * 32 + tx;
                var yIndex = by * 32 + ty;

                for (int i = 0; i < 32; i += 8)
                {
                    if (xIndex < width && yIndex + i < height)
                    {
                        sMem[(ty + i) * 33 + tx] = idata[(yIndex + i) * width + xIndex];
                    }
                }
            }

            Gpu.SyncThreads();

            if (ThreadIndex.X < 256)
            {
                var tx = ThreadIndex.X % 32;
                var ty = ThreadIndex.X / 32;

                var outXIndex = by * 32 + tx;
                var outYIndex = bx * 32 + ty;

                for (int i = 0; i < 32; i += 8)
                {
                    if (outXIndex < height && outYIndex + i < width)
                    {
                        odata[(outYIndex + i) * height + outXIndex] = sMem[tx * 33 + (ty + i)];
                    }
                }
            }

            if (syncThreads)
            {
                Gpu.SyncThreads();
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords,
        bool syncThreads = true)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        Gpu.GridStrideLoop(numWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

            var w0 = src[safeIdx0];
            var w1 = src[safeIdx1];

            var mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
            var mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            var shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            dst[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    /// <summary>
    /// Returns word <paramref name="index"/> of the quotient <c>q = source &gt;&gt; (32 * FoldWord + FoldBit)</c>, reading words
    /// past <see cref="QuadraticFoldBlockedPassContext.SourceWords"/> as 0, and 0 for a negative index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GpuBigIntKernels.GetShiftedRightWord"/> shifts the whole value, so its word -1 holds the top bits of the
    /// remainder below the fold; as a factor of <c>q * 2^E</c> and <c>t * q</c> those words must be 0.
    /// </para>
    /// <para>
    /// <see cref="QuadraticFoldBlockedPassContext.Source"/> is not <c>[GpuReadOnly]</c> (and this helper does not call the
    /// <c>[GpuReadOnly]</c> <see cref="GpuBigIntKernels.GetShiftedRightWord"/>): the second and third folds read the
    /// temporaries that the fold before them wrote in the same launch, so the reads must not go through the read-only
    /// (non-coherent) data path.
    /// </para>
    /// </remarks>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetQuotientWord(
        QuadraticFoldBlockedPassContext ctx, QuadraticFoldSliceOffsets offsets, int index)
    {
        if (index < 0)
        {
            return 0u;
        }

        var low = index + offsets.FoldWord;
        uint w0 = low < ctx.SourceWords ? ctx.Source[low] : 0u;
        if (offsets.FoldBit == 0)
        {
            return w0;
        }

        uint w1 = low + 1 < ctx.SourceWords ? ctx.Source[low + 1] : 0u;
        return (w0 >> offsets.FoldBit) | (w1 << (32 - offsets.FoldBit));
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeQuadraticFoldLocalSum(
        QuadraticFoldBlockedPassContext ctx, QuadraticFoldSliceOffsets offsets, int idx)
    {
        // r_i: the bits of source below m.
        uint word = idx < ctx.SourceWords ? ctx.Source[idx] : 0u;
        uint remainder = idx < offsets.FoldWord
            ? word
            : (idx == offsets.FoldWord ? word & ((1u << offsets.FoldBit) - 1u) : 0u);

        // q_j is word j of source >> m (0 for j < 0). (q << E)_i comes from q_(i - E/32) and q_(i - E/32 - 1);
        // a shift by 32 would be undefined, so E % 32 == 0 takes the word as it is.
        uint quotientHigh = GetQuotientWord(ctx, offsets, idx - offsets.ShiftWord);
        uint quotientLow = GetQuotientWord(ctx, offsets, idx - offsets.ShiftWord - 1);
        uint shiftedQuotient = offsets.ShiftBit == 0
            ? quotientHigh
            : (quotientHigh << offsets.ShiftBit) | (quotientLow >> (32 - offsets.ShiftBit));

        // A_i = low(t * q_i), B_i = high(t * q_(i-1)).
        uint quotientHere = GetQuotientWord(ctx, offsets, idx);
        uint quotientBelow = GetQuotientWord(ctx, offsets, idx - 1);
        ulong termHere = (ulong)quotientHere * ctx.ConstantTerm;
        ulong termBelow = (ulong)quotientBelow * ctx.ConstantTerm;

        return (ulong)remainder
               + shiftedQuotient
               + (~(uint)termHere)
               + (~(uint)(termBelow >> 32));
    }

    [GpuKernel]
    public static void KernelTestBlockAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign)
    {
        GpuBigIntKernels.DeviceAddSubParallel(a, b, result, numWords, sign);
    }

    /// <summary>
    /// Evaluates one fold pass <c>result = r + q * 2^E - t * q</c> in parallel chunks of one launch block across all SMs,
    /// writing the outgoing carry (<c>0..4</c>) of each chunk <c>b</c> to <see cref="QuadraticFoldBlockedPassContext.BlockCarries"/>.
    /// </summary>
    /// <remarks>
    /// <c>t * q</c> is split into two numbers whose words need no carry: <c>A</c> with word <c>i</c> the low half of the
    /// 64-bit product <c>t * q_i</c>, and <c>B</c> with word <c>i</c> the high half of <c>t * q_(i-1)</c>, so that
    /// <c>t * q = A + B</c>. Each is subtracted as its complement plus 1 (two's complement), so each word adds four 32-bit
    /// terms and chunk 0 starts its running high word at 2.
    /// </remarks>
    [GpuDeviceFunction]
    public static void ExecuteQuadraticFoldPassBlocked(QuadraticFoldBlockedPassContext ctx)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int laneId = tid & 31;
        int warpId = tid >> 5;
        int numWarps = (bDim + 31) >> 5;
        int numBlocks = (ctx.ResultWords + bDim - 1) / bDim;

        var sharedCarry = new SharedMemory<uint>(32);
        var sharedOverlapH = new SharedMemory<uint>(32);

        var offsets = new QuadraticFoldSliceOffsets
        {
            FoldWord = ctx.ModulusBits / 32,
            FoldBit = ctx.ModulusBits % 32,
            ShiftWord = ctx.Exponent / 32,
            ShiftBit = ctx.Exponent % 32
        };

        for (int bIdx = BlockIndex.X; bIdx < numBlocks; bIdx += GridDimension.X)
        {
            int idx = bIdx * bDim + tid;
            bool valid = idx < ctx.ResultWords;

            ulong localSum = valid
                ? ComputeQuadraticFoldLocalSum(ctx, offsets, idx)
                : 0UL;

            uint lowWord = (uint)(localSum & 0xFFFFFFFFUL);
            uint highWord = (uint)(localSum >> 32);

            uint prevH = GpuBigIntNttKernels.ResolveOverlapH(
                highWord, sharedOverlapH, laneId, warpId, bIdx == 0 ? 2u : 0u);

            ulong sum = (ulong)lowWord + prevH;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            uint carryIn = GpuBigIntNttKernels.ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim);

            if (valid)
            {
                ctx.Result[idx] = (uint)sum + carryIn;
            }

            if (tid == 0)
            {
                uint lastH = sharedOverlapH[numWarps - 1];
                uint waveCarry = GpuBigIntKernels.ApplyCarry(
                    GpuBigIntKernels.UnpackCarryState(sharedCarry[numWarps - 1]),
                    0u);
                ctx.BlockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    /// <summary>
    /// Multi-block parallel kernel for one trinomial fold pass. Follow with <see cref="QuadraticFoldPassApplyKernel"/>
    /// when <see cref="QuadraticFoldBlockedPassContext.ResultWords"/> spans more than one block.
    /// </summary>
    [GpuKernel]
    public static void QuadraticFoldPassBlockedKernel(QuadraticFoldBlockedPassContext ctx)
    {
        ExecuteQuadraticFoldPassBlocked(ctx);
    }

    /// <summary>
    /// Applies the per-chunk carries written by <see cref="QuadraticFoldPassBlockedKernel"/> into the head of each
    /// following chunk modulo <c>2^(32 * ResultWords)</c>.
    /// </summary>
    [GpuKernel]
    public static void QuadraticFoldPassApplyKernel(QuadraticFoldBlockedApplyContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumBlocks, globalBlockIdx =>
        {
            var carry = ctx.BlockCarries[globalBlockIdx];
            if (carry > 0)
            {
                int startIdx = (globalBlockIdx + 1) * ctx.ChunkSize;
                GpuBigIntNttKernels.AtomicAddWithCarry(ctx.Result, startIdx, carry, ctx.ResultWords);
            }
        });
    }
}
