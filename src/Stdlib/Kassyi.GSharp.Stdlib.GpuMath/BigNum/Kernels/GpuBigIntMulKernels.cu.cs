namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// Defines CUDA GPU device kernels for large-integer multiplication using NTT, Comba, and Karatsuba algorithms.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Emits device-side kernels transpiled via GSharp source generators for parallel multiplication pipelines.
/// </para>
/// <para>
/// <b>Execution Target:</b> GPU Device Kernels (<c>[GpuKernel]</c> / <c>[GpuDevice]</c>).
/// </para>
/// </remarks>
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntMulKernels
{
    // Global Non-Cooperative NTT approach for Multi-Million digit multiplications
    public struct NttUnpackOperandContext
    {
        [GpuReadOnly]
        public uint[] Source;

        public uint[] NttBuf1;
        public uint[] NttBuf2;
        public int NumWords;
    }

    public struct NttUnpackContext
    {
        public NttUnpackOperandContext OperandA;
        public NttUnpackOperandContext OperandB;
        public NttGridContext Grid;
    }

    public struct NttUnpackSingleContext
    {
        public NttUnpackOperandContext Operand;
        public NttGridContext Grid;
    }

    [GpuKernel]
    public static void MultiplyNttUnpackKernel(NttUnpackContext ctx)
    {
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(
            ctx.OperandA.Source, ctx.OperandA.NttBuf1, ctx.OperandA.NttBuf2,
            ctx.Grid.M, ctx.OperandA.NumWords, ctx.Grid.N1, ctx.Grid.N2, true);
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(
            ctx.OperandB.Source, ctx.OperandB.NttBuf1, ctx.OperandB.NttBuf2,
            ctx.Grid.M, ctx.OperandB.NumWords, ctx.Grid.N1, ctx.Grid.N2, true);
    }

    [GpuKernel]
    public static void MultiplyNttUnpackSingleKernel(NttUnpackSingleContext ctx)
    {
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(
            ctx.Operand.Source, ctx.Operand.NttBuf1, ctx.Operand.NttBuf2,
            ctx.Grid.M, ctx.Operand.NumWords, ctx.Grid.N1, ctx.Grid.N2, true);
    }

    [GpuKernel]
    public static void MultiplyNttSharedRowKernel(NumberTheoreticTransform args)
    {
        GpuBigIntNttKernels.ExecuteNttSharedRow(args);
    }

    [GpuKernel]
    public static void MultiplyNttTablelessSharedRowKernel(GpuBigIntNttKernels.NttTablelessRowContext args)
    {
        GpuBigIntNttKernels.ExecuteNttTablelessSharedRow(args);
    }

    [GpuKernel]
    public static void MultiplyNttFusedMiddleSquareRowKernel(GpuBigIntNttKernels.NttFusedMiddleSquareContext args)
    {
        GpuBigIntNttKernels.ExecuteNttFusedMiddleSquareRow(args);
    }

    public struct Ntt35UnpackOperandContext
    {
        [GpuReadOnly]
        public uint[] Source;

        public uint[] NttBuf1;
        public uint[] NttBuf2;
        public uint[] NttBuf3;
        public int NumWords;
    }

    public struct Ntt35UnpackContext
    {
        public Ntt35UnpackOperandContext OperandA;
        public Ntt35UnpackOperandContext OperandB;
        public NttGridContext Grid;
    }

    public struct Ntt35UnpackSingleContext
    {
        public Ntt35UnpackOperandContext Operand;
        public NttGridContext Grid;
    }

    public struct Ntt35PointwiseMultiplyContext
    {
        public uint[] Buf1A;
        public uint[] Buf2A;
        public uint[] Buf3A;

        [GpuReadOnly]
        public uint[] Buf1B;

        [GpuReadOnly]
        public uint[] Buf2B;

        [GpuReadOnly]
        public uint[] Buf3B;

        public int TransformLength;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ExtractLimb35([GpuReadOnly] uint[] source, int numWords, int j)
    {
        ulong bitPos = (ulong)(uint)j * 35UL;
        int wordIdx = (int)(bitPos >> 5);
        if (wordIdx >= numWords)
        {
            return 0UL;
        }

        int bitOffset = (int)(bitPos & 31UL);
        ulong w0 = source[wordIdx];
        ulong w1 = wordIdx + 1 < numWords ? source[wordIdx + 1] : 0UL;
        if (bitOffset == 0)
        {
            return w0 | ((w1 & 0x7UL) << 32);
        }

        ulong w2 = (bitOffset > 29 && wordIdx + 2 < numWords) ? source[wordIdx + 2] : 0UL;
        return ((w0 >> bitOffset) | (w1 << (32 - bitOffset)) | (w2 << (64 - bitOffset))) & 0x7FFFFFFFFUL;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExecuteUnpack35Small(Ntt35UnpackOperandContext operand, int m)
    {
        int numWords = operand.NumWords;
        int stride = GridDimension.X * BlockDimension.X;
        for (int j = BlockIndex.X * BlockDimension.X + ThreadIndex.X; j < m; j += stride)
        {
            ulong val = ExtractLimb35(operand.Source, numWords, j);
            operand.NttBuf1[j] = val == 0UL
                ? 0u
                : GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime1,
                    GpuBigIntNttKernels.BarrettMagic1);
            operand.NttBuf2[j] = val == 0UL
                ? 0u
                : GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime2,
                    GpuBigIntNttKernels.BarrettMagic2);
            operand.NttBuf3[j] = val == 0UL
                ? 0u
                : GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime3,
                    GpuBigIntNttKernels.BarrettMagic3);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteUnpack35Tiled32x32(Ntt35UnpackOperandContext operand, NttGridContext grid)
    {
        int tid = ThreadIndex.X;
        int tx = tid & 31;
        int ty = tid >> 5;
        int n1 = grid.N1;
        int n2 = grid.N2;
        int numWords = operand.NumWords;

        int tilesX = n2 >> 5;
        int tilesY = n1 >> 5;
        int totalTiles = tilesX * tilesY;
        int tilesXMask = tilesX - 1;
        int log2TilesX = 31 - (int)Gpu.CountLeadingZeros((uint)tilesX);
        int log2N2 = log2TilesX + 5;

        // 32 x 33 padded shared tile for 35-bit limbs (split into 32-bit low + 3-bit high = 8.25 KB, zero bank conflicts).
        var sTileLo = new SharedMemory<uint>(1056);
        var sTileHi = new SharedMemory<uint>(1056);

        for (int tileIdx = BlockIndex.X; tileIdx < totalTiles; tileIdx += GridDimension.X)
        {
            int bx = tileIdx & tilesXMask;
            int by = tileIdx >> log2TilesX;
            int r0 = bx << 5;
            int c0 = by << 5;

            // Phase 1: Coalesced read (tx contiguous -> jIn contiguous -> wordIdx spans <= 35 contiguous words).
            int rIn = r0 + tx;
            int cIn = c0 + ty;
            int jIn = (cIn << log2N2) + rIn;
            ulong valIn = ExtractLimb35(operand.Source, numWords, jIn);

            int inSlot = ty * 33 + tx;
            sTileLo[inSlot] = (uint)(valIn & 0xFFFFFFFFUL);
            sTileHi[inSlot] = (uint)(valIn >> 32);

            Gpu.SyncThreads();

            // Phase 2: Transposed read + coalesced write (tx contiguous -> cOut contiguous -> dstIdx contiguous).
            int rOut = r0 + ty;
            int cOut = c0 + tx;
            int dstIdx = rOut * n1 + cOut;
            int outSlot = tx * 33 + ty;
            ulong val = sTileLo[outSlot] | ((ulong)sTileHi[outSlot] << 32);

            uint r1 = 0u;
            uint r2 = 0u;
            uint r3 = 0u;
            if (val != 0UL)
            {
                r1 = GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime1,
                    GpuBigIntNttKernels.BarrettMagic1);
                r2 = GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime2,
                    GpuBigIntNttKernels.BarrettMagic2);
                r3 = GpuBigIntNttKernels.ModReduce64Barrett(val, GpuBigIntNttKernels.Prime3,
                    GpuBigIntNttKernels.BarrettMagic3);
            }

            operand.NttBuf1[dstIdx] = r1;
            operand.NttBuf2[dstIdx] = r2;
            operand.NttBuf3[dstIdx] = r3;

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSmallKernel(Ntt35UnpackContext ctx)
    {
        ExecuteUnpack35Small(ctx.OperandA, ctx.Grid.M);
        ExecuteUnpack35Small(ctx.OperandB, ctx.Grid.M);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSingleSmallKernel(Ntt35UnpackSingleContext ctx)
    {
        ExecuteUnpack35Small(ctx.Operand, ctx.Grid.M);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackTiledKernel(Ntt35UnpackContext ctx)
    {
        ExecuteUnpack35Tiled32x32(ctx.OperandA, ctx.Grid);
        ExecuteUnpack35Tiled32x32(ctx.OperandB, ctx.Grid);
    }

    [GpuKernel]
    public static void MultiplyNtt35UnpackSingleTiledKernel(Ntt35UnpackSingleContext ctx)
    {
        ExecuteUnpack35Tiled32x32(ctx.Operand, ctx.Grid);
    }

    [GpuKernel]
    public static void MultiplyNtt35PointwiseMultiplyKernel(Ntt35PointwiseMultiplyContext ctx)
    {
        int m = ctx.TransformLength;
        int stride = GridDimension.X * BlockDimension.X;
        for (int index = BlockIndex.X * BlockDimension.X + ThreadIndex.X; index < m; index += stride)
        {
            ctx.Buf1A[index] = GpuBigIntNttKernels.ModMul1(ctx.Buf1A[index], ctx.Buf1B[index]);
            ctx.Buf2A[index] = GpuBigIntNttKernels.ModMul2(ctx.Buf2A[index], ctx.Buf2B[index]);
            ctx.Buf3A[index] = GpuBigIntNttKernels.ModMul3(ctx.Buf3A[index], ctx.Buf3B[index]);
        }
    }

    [GpuKernel]
    public static void MultiplyNttTransposeKernel(uint[] input, uint[] output, int width, int height)
    {
        GpuBigIntFermatKernels.DeviceTranspose32X32(input, output, width, height, true);
    }

    [GpuKernel]
    public static void MultiplyNttPointwiseMultiplyKernel(
        uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, int transformLength)
    {
        GpuBigIntNttKernels.ExecuteNttPointwiseMultiply(
            nttBuf1A, nttBuf2A, nttBuf1B, nttBuf2B, transformLength);
    }

    [GpuKernel]
    public static void MultiplyNttPointwiseSquareKernel(
        uint[] nttBuf1A, uint[] nttBuf2A, int transformLength)
    {
        GpuBigIntNttKernels.ExecuteNttPointwiseSquare(
            nttBuf1A, nttBuf2A, transformLength);
    }

    [GpuKernel]
    public static void MultiplyNttComputeCrtKernel(
        uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength,
        int n1, int n2)
    {
        GpuBigIntNttKernels.ExecuteCrtComputeCoefficientsTransposed(
            deviceResult1, deviceResult2, uncarriedLow, uncarriedHigh, transformLength, n1, n2);
    }

    /// <summary>
    /// Performs single-block cooperative Number Theoretic Transform (NTT) multiplication (<c>result = a * b</c>)
    /// in shared memory for operands whose combined output length fits within 512 32-bit words (1024 16-bit half-words).
    /// </summary>
    /// <param name="a">The first multi-limb unsigned integer operand.</param>
    /// <param name="b">The second multi-limb unsigned integer operand.</param>
    /// <param name="result">The destination buffer receiving the <c>a.Length + b.Length</c>-word product.</param>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiplyNtt(
        [GpuReadOnly] Span<uint> a,
        [GpuReadOnly] Span<uint> b,
        Span<uint> result)
    {
        MultiplyNtt(a, a.Length, b, b.Length, result, result.Length);
    }

    /// <summary>
    /// Performs single-block cooperative Number Theoretic Transform (NTT) multiplication (<c>result = a * b</c>)
    /// in shared memory for explicit operand and result word counts.
    /// </summary>
    /// <param name="a">The first multi-limb unsigned integer operand.</param>
    /// <param name="numWordsA">The number of 32-bit words in <paramref name="a"/>.</param>
    /// <param name="b">The second multi-limb unsigned integer operand.</param>
    /// <param name="numWordsB">The number of 32-bit words in <paramref name="b"/>.</param>
    /// <param name="result">The destination buffer receiving the product.</param>
    /// <param name="numWordsResult">The number of 32-bit words in <paramref name="result"/>.</param>
    [GpuDeviceFunction]
    public static void MultiplyNtt(
        [GpuReadOnly] Span<uint> a,
        int numWordsA,
        [GpuReadOnly] Span<uint> b,
        int numWordsB,
        Span<uint> result,
        int numWordsResult)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;

        var sNtt1A = new SharedMemory<uint>(1024);
        var sNtt2A = new SharedMemory<uint>(1024);
        var sNttB = new SharedMemory<uint>(1024);
        var sCarry = new SharedMemory<uint>(32);
        var sOverlapH = new SharedMemory<uint>(32);

        int targetHalfWords = 2 * (numWordsA + numWordsB);
        int m = 4;
        int log2M = 2;
        while (m < targetHalfWords && m < 1024)
        {
            m <<= 1;
            log2M++;
        }

        // Step 1: Unpack 32-bit limbs of A and B into 16-bit half-words at bit-reversed positions for Prime1
        for (int k = tid; k < m; k += bDim)
        {
            int wordIdx = k >> 1;
            int shift = (k & 1) << 4;
            uint valA = wordIdx < numWordsA ? (a[wordIdx] >> shift) & 0xFFFFu : 0u;
            uint valB = wordIdx < numWordsB ? (b[wordIdx] >> shift) & 0xFFFFu : 0u;
            uint revK = Gpu.BitReverse((uint)k) >> (32 - log2M);
            sNtt1A[(int)revK] = valA;
            sNtt2A[(int)revK] = valA;
            sNttB[(int)revK] = valB;
        }

        Gpu.SyncThreads();

        // Step 2: Forward NTT for Prime1 on sNtt1A and sNttB
        ExecuteSharedButterflies(sNtt1A, m, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.NttG1,
            GpuBigIntNttKernels.BarrettMagic1, false, tid, bDim);
        ExecuteSharedButterflies(sNttB, m, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.NttG1,
            GpuBigIntNttKernels.BarrettMagic1, false, tid, bDim);

        for (int k = tid; k < m; k += bDim)
        {
            sNtt1A[k] = GpuBigIntNttKernels.ModMul1(sNtt1A[k], sNttB[k]);
        }

        Gpu.SyncThreads();

        // Step 3: Unpack B for Prime2, run Forward NTT for Prime2 on sNtt2A and sNttB
        for (int k = tid; k < m; k += bDim)
        {
            int wordIdx = k >> 1;
            int shift = (k & 1) << 4;
            uint valB = wordIdx < numWordsB ? (b[wordIdx] >> shift) & 0xFFFFu : 0u;
            uint revK = Gpu.BitReverse((uint)k) >> (32 - log2M);
            sNttB[(int)revK] = valB;
        }

        Gpu.SyncThreads();

        ExecuteSharedButterflies(sNtt2A, m, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.NttG2,
            GpuBigIntNttKernels.BarrettMagic2, false, tid, bDim);
        ExecuteSharedButterflies(sNttB, m, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.NttG2,
            GpuBigIntNttKernels.BarrettMagic2, false, tid, bDim);

        for (int k = tid; k < m; k += bDim)
        {
            sNtt2A[k] = GpuBigIntNttKernels.ModMul2(sNtt2A[k], sNttB[k]);
        }

        Gpu.SyncThreads();

        // Step 4: Bit-reverse in-place and run Inverse NTT on sNtt1A (Prime1) and sNtt2A (Prime2)
        BitReverseSharedInPlace(sNtt1A, sNtt2A, m, log2M, tid, bDim);

        ExecuteSharedButterflies(sNtt1A, m, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.NttG1,
            GpuBigIntNttKernels.BarrettMagic1, true, tid, bDim);
        ExecuteSharedButterflies(sNtt2A, m, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.NttG2,
            GpuBigIntNttKernels.BarrettMagic2, true, tid, bDim);

        // Step 5: Chinese Remainder Theorem (CRT) recombination into 64-bit convolution coefficients (low in sNtt1A, high in sNtt2A)
        uint invN1 = GpuBigIntNttKernels.ModPow((uint)m, GpuBigIntNttKernels.Prime1 - 2u, GpuBigIntNttKernels.Prime1,
            GpuBigIntNttKernels.BarrettMagic1);
        uint invN2 = GpuBigIntNttKernels.ModPow((uint)m, GpuBigIntNttKernels.Prime2 - 2u, GpuBigIntNttKernels.Prime2,
            GpuBigIntNttKernels.BarrettMagic2);

        for (int k = tid; k < m; k += bDim)
        {
            const ulong Modulus1 = GpuBigIntNttKernels.Prime1;
            const ulong Modulus2 = GpuBigIntNttKernels.Prime2;
            const ulong Prime1InverseModPrime2 = 1050101200UL;

            ulong v1 = GpuBigIntNttKernels.ModMul1(sNtt1A[k], invN1);
            ulong v2 = GpuBigIntNttKernels.ModMul2(sNtt2A[k], invN2);
            ulong v1Mod2 = v1 % Modulus2;
            ulong diff = v2 >= v1Mod2 ? v2 - v1Mod2 : v2 + Modulus2 - v1Mod2;
            ulong crtCoeff = (diff * Prime1InverseModPrime2) % Modulus2;
            ulong recombined = v1 + Modulus1 * crtCoeff;

            sNtt1A[k] = (uint)(recombined & 0xFFFFFFFFUL);
            sNtt2A[k] = (uint)(recombined >> 32);
        }

        Gpu.SyncThreads();

        // Step 6: Carry resolution across 16-bit half-word convolution coefficients into 32-bit output words
        ResolveSharedNttCarries(sNtt1A, sNtt2A, result, numWordsResult, m, sCarry, sOverlapH, tid, bDim);
    }

    /// <summary>
    /// GPU kernel entry point for single-block cooperative NTT multiplication.
    /// </summary>
    [GpuKernel]
    public static void MultiplyNttKernel(
        uint[] result,
        int numWordsResult,
        [GpuReadOnly] uint[] a,
        int numWordsA,
        [GpuReadOnly] uint[] b,
        int numWordsB)
    {
        MultiplyNtt(a, numWordsA, b, numWordsB, result, numWordsResult);
    }

    [GpuDeviceFunction]
    private static void ExecuteSharedButterflies(
        SharedMemory<uint> buffer,
        int m,
        uint prime,
        uint primitiveRoot,
        ulong barrettMagic,
        bool isInverse,
        int tid,
        int bDim)
    {
        int halfM = m >> 1;
        for (int length = 2; length <= m; length <<= 1)
        {
            int halfLen = length >> 1;
            uint wLen = GpuBigIntNttKernels.ModPow(primitiveRoot, (prime - 1u) / (uint)length, prime, barrettMagic);
            if (isInverse)
            {
                wLen = GpuBigIntNttKernels.ModPow(wLen, prime - 2u, prime, barrettMagic);
            }

            for (int pairIdx = tid; pairIdx < halfM; pairIdx += bDim)
            {
                int groupIdx = pairIdx / halfLen;
                int elemIdx = pairIdx & (halfLen - 1);
                int idx1 = groupIdx * length + elemIdx;
                int idx2 = idx1 + halfLen;

                uint w = GpuBigIntNttKernels.ModPow(wLen, (uint)elemIdx, prime, barrettMagic);
                uint u = buffer[idx1];
                uint v = GpuBigIntNttKernels.ModMulBarrett(buffer[idx2], w, prime, barrettMagic);

                buffer[idx1] = GpuBigIntNttKernels.ModAdd(u, v, prime);
                buffer[idx2] = GpuBigIntNttKernels.ModSub(u, v, prime);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    private static void BitReverseSharedInPlace(
        SharedMemory<uint> buf1,
        SharedMemory<uint> buf2,
        int m,
        int log2M,
        int tid,
        int bDim)
    {
        for (int k = tid; k < m; k += bDim)
        {
            int revK = (int)(Gpu.BitReverse((uint)k) >> (32 - log2M));
            if (k < revK)
            {
                uint tmp1 = buf1[k];
                buf1[k] = buf1[revK];
                buf1[revK] = tmp1;

                uint tmp2 = buf2[k];
                buf2[k] = buf2[revK];
                buf2[revK] = tmp2;
            }
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    private static void ResolveSharedNttCarries(
        SharedMemory<uint> uncarriedLow,
        SharedMemory<uint> uncarriedHigh,
        Span<uint> result,
        int numWordsResult,
        int m,
        SharedMemory<uint> sCarry,
        SharedMemory<uint> sOverlapH,
        int tid,
        int bDim)
    {
        int laneId = tid & 31;
        int warpId = tid >> 5;
        int numWarps = (bDim + 31) >> 5;

        uint runningH = 0u;
        uint runningCarry = 0u;

        for (int baseWord = 0; baseWord < numWordsResult; baseWord += bDim)
        {
            int i = baseWord + tid;
            ulong localSum = 0UL;
            if (i < numWordsResult)
            {
                for (int step = 0; step < 5; step++)
                {
                    int idx = 2 * i + 1 - step;
                    if (idx >= 0 && idx < m)
                    {
                        ulong c = uncarriedLow[idx] | ((ulong)uncarriedHigh[idx] << 32);
                        if (step == 0)
                        {
                            localSum += (c & 0xFFFFUL) << 16;
                        }
                        else
                        {
                            localSum += (c >> ((step - 1) * 16)) & 0xFFFFFFFFUL;
                        }
                    }
                }
            }

            uint lowWord = (uint)(localSum & 0xFFFFFFFFUL);
            uint highWord = (uint)(localSum >> 32);

            uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, highWord, 1u);
            if (laneId == 31)
            {
                sOverlapH[warpId] = highWord;
            }

            Gpu.SyncThreads();

            if (laneId == 0)
            {
                prevH = warpId == 0 ? runningH : sOverlapH[warpId - 1];
            }

            ulong sum = (ulong)lowWord + prevH;
            uint g = i < numWordsResult ? (uint)(sum >> 32) : 0u;
            uint p = i < numWordsResult ? ((uint)sum == 0xFFFFFFFFu ? 1u : 0u) : 1u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
            if (laneId == 31)
            {
                sCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
            }

            Gpu.SyncThreads();

            if (warpId == 0)
            {
                var valState = laneId < numWarps
                    ? GpuBigIntKernels.UnpackCarryState(sCarry[laneId])
                    : new GpuBigIntKernels.CarryState(0u, 1u);
                var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);
                if (laneId < numWarps)
                {
                    sCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
                }
            }

            Gpu.SyncThreads();

            uint carryIn = runningCarry;
            if (warpId > 0)
            {
                carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sCarry[warpId - 1]),
                    runningCarry);
            }

            uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
            uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Propagate, 1u);
            if (laneId > 0)
            {
                carryIn = prevScanG | (prevScanP & carryIn);
            }

            if (i < numWordsResult)
            {
                result[i] = (uint)sum + carryIn;
            }

            uint nextRunningH = sOverlapH[numWarps - 1];
            uint nextRunningCarry =
                GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sCarry[numWarps - 1]), runningCarry);

            Gpu.SyncThreads();

            runningH = nextRunningH;
            runningCarry = nextRunningCarry;
        }
    }
}
