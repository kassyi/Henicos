namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntFermatKernels
{
    /// <summary>
    /// The buffers and sizes of <see cref="QuadraticFoldReduceKernel"/>, which reduces the square of a residue modulo
    /// <c>N = 2^m - c</c> (<c>m = 2E + 1</c>, <c>c = 2^E - ConstantTerm</c>).
    /// </summary>
    public struct QuadraticFoldContext
    {
        /// <summary>The square to reduce, <see cref="ProductWords"/> words.</summary>
        /// <remarks>Only read, but not <c>[GpuReadOnly]</c>: it is passed to <see cref="QuadraticFoldPass"/>, whose source is
        /// writable memory in the later folds.</remarks>
        public uint[] Product;

        /// <summary>The output of the first fold, <see cref="FirstFoldWords"/> words.</summary>
        public uint[] FirstFold;

        /// <summary>The output of the second fold, <see cref="SecondFoldWords"/> words.</summary>
        public uint[] SecondFold;

        /// <summary>Receives the output of the third fold, <see cref="ResidueWords"/> words.</summary>
        public uint[] Residue;

        /// <summary>The word count of <see cref="Product"/>.</summary>
        public int ProductWords;

        /// <summary>The word count of <see cref="FirstFold"/>: <c>m + E + 3</c> bits.</summary>
        public int FirstFoldWords;

        /// <summary>The word count of <see cref="SecondFold"/>: <c>m + 2</c> bits.</summary>
        public int SecondFoldWords;

        /// <summary>The word count of <see cref="Residue"/>: <c>ceil(m / 32)</c>.</summary>
        public int ResidueWords;

        /// <summary>The fold position <c>m = 2E + 1</c>.</summary>
        public int ModulusBits;

        /// <summary>The exponent <c>E</c>.</summary>
        public int Exponent;

        /// <summary>The constant term <c>t</c> of <c>c = 2^E - t</c> (11).</summary>
        public uint ConstantTerm;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords,
        bool syncThreads = true)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint val = 0;
            var wordIdx = i / 2;
            if (wordIdx < numWords)
            {
                var w = dSrc[wordIdx];
                if (i % 2 == 0)
                {
                    val = w & 0xFFFF;
                }
                else
                {
                    val = w >> 16;
                }
            }

            dDst1[i] = val;
            dDst2[i] = val;
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16Transposed([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n,
        int numWords, int n1, int n2, bool syncThreads = true)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint val = 0;
            var wordIdx = i / 2;
            if (wordIdx < numWords)
            {
                var w = dSrc[wordIdx];
                if (i % 2 == 0)
                {
                    val = w & 0xFFFF;
                }
                else
                {
                    val = w >> 16;
                }
            }

            var dstIdx = n > 512 ? (i % n2) * n1 + (i / n2) : i;
            dDst1[dstIdx] = val;
            dDst2[dstIdx] = val;
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
        Gpu.InlinePtx("extern __shared__ uint32_t s_mem[];");

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

                Gpu.ForUnroll(0, 4, iter =>
                {
                    var i = iter * 8;
                    if (xIndex < width && yIndex + i < height)
                    {
                        Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
                    }
                });
            }

            Gpu.SyncThreads();

            if (ThreadIndex.X < 256)
            {
                var tx = ThreadIndex.X % 32;
                var ty = ThreadIndex.X / 32;

                var outXIndex = by * 32 + tx;
                var outYIndex = bx * 32 + ty;

                Gpu.ForUnroll(0, 4, iter =>
                {
                    var i = iter * 8;
                    if (outXIndex < height && outYIndex + i < width)
                    {
                        uint val = 0;
                        Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
                        odata[(outYIndex + i) * height + outXIndex] = val;
                    }
                });
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

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetShiftedLeftWord(
        [GpuReadOnly] uint[] data,
        int numWords,
        int destIdx,
        int wordShift,
        int bitShift)
    {
        var srcIdx0 = destIdx - wordShift;
        var srcIdx1 = srcIdx0 - 1;

        var w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? data[srcIdx0] : 0u;
        if (bitShift == 0)
        {
            return w0;
        }

        var w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? data[srcIdx1] : 0u;
        return (w0 << bitShift) | (w1 >> (32 - bitShift));
    }

    /// <summary>
    /// Returns word <paramref name="index"/> of the quotient <c>q = source &gt;&gt; (32 * foldWord + foldBit)</c>, reading words
    /// past <paramref name="sourceWords"/> as 0, and 0 for a negative index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GpuBigIntKernels.GetShiftedRightWord"/> shifts the whole value, so its word -1 holds the top bits of the
    /// remainder below the fold; as a factor of <c>q * 2^E</c> and <c>t * q</c> those words must be 0.
    /// </para>
    /// <para>
    /// <paramref name="source"/> is not <c>[GpuReadOnly]</c> (and this helper does not call the <c>[GpuReadOnly]</c>
    /// <see cref="GpuBigIntKernels.GetShiftedRightWord"/>): the second and third folds read the temporaries that the fold
    /// before them wrote in the same launch, so the reads must not go through the read-only (non-coherent) data path.
    /// </para>
    /// </remarks>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetQuotientWord(uint[] source, int sourceWords, int index, int foldWord, int foldBit)
    {
        if (index < 0)
        {
            return 0u;
        }

        var low = index + foldWord;
        uint w0 = low < sourceWords ? source[low] : 0u;
        if (foldBit == 0)
        {
            return w0;
        }

        uint w1 = low + 1 < sourceWords ? source[low + 1] : 0u;
        return (w0 >> foldBit) | (w1 << (32 - foldBit));
    }

    /// <summary>
    /// One fold of a value modulo <c>N = 2^m - c</c> with <c>c = 2^E - t</c>, run by one block:
    /// <c>result = r + q * 2^E - t * q</c> with <c>r = source mod 2^m</c> and <c>q = floor(source / 2^m)</c>, modulo
    /// <c>2^(32 * resultWords)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>t * q</c> is split into two numbers whose words need no carry: <c>A</c> with word <c>i</c> the low half of the
    /// 64-bit product <c>t * q_i</c>, and <c>B</c> with word <c>i</c> the high half of <c>t * q_(i-1)</c>, so that
    /// <c>t * q = A + B</c>. Each is subtracted as its complement plus 1 (two's complement), so each word adds four 32-bit
    /// terms and the running high word starts at 2. The caller sizes <paramref name="resultWords"/> so that the true result
    /// fits, which makes the reduction modulo <c>2^(32 * resultWords)</c> exact (see <c>QuadraticFoldModulus</c>).
    /// </para>
    /// <para>The words are processed in chunks of one block; each chunk resolves its carries with a warp and block scan.</para>
    /// </remarks>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QuadraticFoldPass(
        // Not [GpuReadOnly]: see GetQuotientWord (the later folds read what the fold before them wrote in this launch).
        uint[] source,
        int sourceWords,
        uint[] result,
        int resultWords,
        int modulusBits,
        int exponent,
        uint constantTerm)
    {
        var tid = ThreadIndex.X;
        var bDim = BlockDimension.X;
        var laneId = tid % 32;
        var warpId = tid / 32;
        var numWarps = (bDim + 31) / 32;

        var sharedCarry = new SharedMemory<uint>(32);
        var sharedOverlapH = new SharedMemory<uint>(32);

        // q = source >> m and the shift q << E, each as a word index and a bit offset.
        var foldWord = modulusBits / 32;
        var foldBit = modulusBits % 32;
        var shiftWord = exponent / 32;
        var shiftBit = exponent % 32;

        // +1 for each of the two complemented subtrahends A and B.
        uint runningH = 2u;
        uint runningCarry = 0u;

        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            var idx = offset + tid;
            var valid = idx < resultWords;

            ulong localSum = 0UL;
            if (valid)
            {
                // r_i: the bits of source below m.
                uint word = idx < sourceWords ? source[idx] : 0u;
                uint remainder = idx < foldWord ? word : (idx == foldWord ? word & ((1u << foldBit) - 1u) : 0u);

                // q_j is word j of source >> m (0 for j < 0). (q << E)_i comes from q_(i - E/32) and q_(i - E/32 - 1);
                // a shift by 32 would be undefined, so E % 32 == 0 takes the word as it is.
                uint quotientHigh = GetQuotientWord(source, sourceWords, idx - shiftWord, foldWord, foldBit);
                uint quotientLow = GetQuotientWord(source, sourceWords, idx - shiftWord - 1, foldWord, foldBit);
                uint shiftedQuotient = shiftBit == 0
                    ? quotientHigh
                    : (quotientHigh << shiftBit) | (quotientLow >> (32 - shiftBit));

                // A_i = low(t * q_i), B_i = high(t * q_(i-1)).
                uint quotientHere = GetQuotientWord(source, sourceWords, idx, foldWord, foldBit);
                uint quotientBelow = GetQuotientWord(source, sourceWords, idx - 1, foldWord, foldBit);
                ulong termHere = (ulong)quotientHere * constantTerm;
                ulong termBelow = (ulong)quotientBelow * constantTerm;

                localSum = (ulong)remainder
                           + shiftedQuotient
                           + (~(uint)termHere)
                           + (~(uint)(termBelow >> 32));
            }

            uint lowWord = (uint)(localSum & 0xFFFFFFFFUL);
            uint highWord = (uint)(localSum >> 32);

            uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, highWord, 1u);
            if (laneId == 31)
            {
                sharedOverlapH[warpId] = highWord;
            }

            Gpu.SyncThreads();

            if (laneId == 0)
            {
                prevH = warpId == 0 ? runningH : sharedOverlapH[warpId - 1];
            }

            ulong sum = (ulong)lowWord + prevH;
            uint g = valid ? (uint)(sum >> 32) : 0u;
            uint p = valid ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            if (laneId == 31)
            {
                sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                var valState = laneId < numWarps
                    ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId])
                    : new GpuBigIntKernels.CarryState(0u, 1u);
                var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);
                if (laneId < numWarps)
                {
                    sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
                }
            }

            Gpu.SyncThreads();

            uint carryIn = runningCarry;
            if (warpId > 0)
            {
                carryIn = GpuBigIntKernels.ApplyCarry(
                    GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]),
                    runningCarry);
            }

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Propagate, 1u);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (valid)
            {
                result[idx] = (uint)sum + carryIn;
            }

            uint nextRunningH = sharedOverlapH[numWarps - 1];
            uint nextRunningCarry = GpuBigIntKernels.ApplyCarry(
                GpuBigIntKernels.UnpackCarryState(sharedCarry[numWarps - 1]),
                runningCarry);

            Gpu.SyncThreads();

            runningH = nextRunningH;
            runningCarry = nextRunningCarry;
        }

        // Publish the result to the next pass of the block.
        Gpu.SyncThreads();
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
    /// Reduces the square of a residue modulo <c>N = 2^m - c</c> (<c>m = 2E + 1</c>, <c>c = 2^E - t</c>) with three folds,
    /// writing a value congruent to the square and below <c>2^m + 2^(E+1)</c> to <see cref="QuadraticFoldContext.Residue"/>.
    /// </summary>
    /// <remarks>
    /// Launch with one block (<c>gridDimX = 1</c>) of a multiple of 32 threads, at most 1024: the passes synchronize the
    /// block between chunks. The product must be the square (or product) of values below <c>2^(m+1)</c>; the bounds that
    /// size the passes and make the result fit are in <c>QuadraticFoldModulus</c>, whose <c>FoldReference</c> is the host
    /// reference of this kernel. The residue may be one of the factors of the product: the product is complete before the
    /// kernel starts and the kernel reads only the product and its own temporaries.
    /// </remarks>
    [GpuKernel]
    public static void QuadraticFoldReduceKernel(QuadraticFoldContext ctx)
    {
        QuadraticFoldPass(ctx.Product, ctx.ProductWords, ctx.FirstFold, ctx.FirstFoldWords, ctx.ModulusBits,
            ctx.Exponent, ctx.ConstantTerm);
        QuadraticFoldPass(ctx.FirstFold, ctx.FirstFoldWords, ctx.SecondFold, ctx.SecondFoldWords, ctx.ModulusBits,
            ctx.Exponent, ctx.ConstantTerm);
        QuadraticFoldPass(ctx.SecondFold, ctx.SecondFoldWords, ctx.Residue, ctx.ResidueWords, ctx.ModulusBits,
            ctx.Exponent, ctx.ConstantTerm);
    }
}



