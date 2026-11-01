namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

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
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(ctx.OperandA, ctx.Grid, true);
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(ctx.OperandB, ctx.Grid, true);
    }

    [GpuKernel]
    public static void MultiplyNttUnpackSingleKernel(NttUnpackSingleContext ctx)
    {
        GpuBigIntFermatKernels.DeviceUnpack16Transposed(ctx.Operand, ctx.Grid, true);
    }

    [GpuKernel]
    public static void MultiplyNttSharedRowKernel(NumberTheoreticTransform args)
    {
        GpuBigIntNttKernels.ExecuteNttSharedRow(args);
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
        ExecuteSharedButterflies(sNtt1A, m, Prime1, NttG1, BarrettMagic1, false, tid, bDim);
        ExecuteSharedButterflies(sNttB, m, Prime1, NttG1, BarrettMagic1, false, tid, bDim);

        for (int k = tid; k < m; k += bDim)
        {
            sNtt1A[k] = ModMul1(sNtt1A[k], sNttB[k]);
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

        ExecuteSharedButterflies(sNtt2A, m, Prime2, NttG2, BarrettMagic2, false, tid, bDim);
        ExecuteSharedButterflies(sNttB, m, Prime2, NttG2, BarrettMagic2, false, tid, bDim);

        for (int k = tid; k < m; k += bDim)
        {
            sNtt2A[k] = ModMul2(sNtt2A[k], sNttB[k]);
        }

        Gpu.SyncThreads();

        // Step 4: Bit-reverse in-place and run Inverse NTT on sNtt1A (Prime1) and sNtt2A (Prime2)
        BitReverseSharedInPlace(sNtt1A, sNtt2A, m, log2M, tid, bDim);

        ExecuteSharedButterflies(sNtt1A, m, Prime1, NttG1, BarrettMagic1, true, tid, bDim);
        ExecuteSharedButterflies(sNtt2A, m, Prime2, NttG2, BarrettMagic2, true, tid, bDim);

        // Step 5: Chinese Remainder Theorem (CRT) recombination into 64-bit convolution coefficients (low in sNtt1A, high in sNtt2A)
        uint invN1 = ModPow((uint)m, Prime1 - 2u, Prime1, BarrettMagic1);
        uint invN2 = ModPow((uint)m, Prime2 - 2u, Prime2, BarrettMagic2);

        for (int k = tid; k < m; k += bDim)
        {
            const ulong Modulus1 = Prime1;
            const ulong Modulus2 = Prime2;
            const ulong Prime1InverseModPrime2 = Prime1InvModPrime2;

            ulong v1 = ModMul1(sNtt1A[k], invN1);
            ulong v2 = ModMul2(sNtt2A[k], invN2);
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
            uint wLen = ModPow(primitiveRoot, (prime - 1u) / (uint)length, prime, barrettMagic);
            if (isInverse)
            {
                wLen = ModPow(wLen, prime - 2u, prime, barrettMagic);
            }

            for (int pairIdx = tid; pairIdx < halfM; pairIdx += bDim)
            {
                int groupIdx = pairIdx / halfLen;
                int elemIdx = pairIdx & (halfLen - 1);
                int idx1 = groupIdx * length + elemIdx;
                int idx2 = idx1 + halfLen;

                uint w = ModPow(wLen, (uint)elemIdx, prime, barrettMagic);
                uint u = buffer[idx1];
                uint v = ModMulBarrett(buffer[idx2], w, prime, barrettMagic);

                buffer[idx1] = ModAdd(u, v, prime);
                buffer[idx2] = ModSub(u, v, prime);
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

            uint prevH = GpuBigIntNttKernels.ResolveOverlapH(highWord, sOverlapH, laneId, warpId, runningH);

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
