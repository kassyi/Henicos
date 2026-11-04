// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using static NttPrimeArithmetic;

/// <summary>
/// Tableless shared-memory row NTT and fused middle-pass squaring kernels for the 3-prime 35-bit convolution pipeline.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class Ntt35TablelessKernels
{
    public struct NttTablelessRowContext
    {
        public uint[] InputBuffer;
        public uint[] OutputBuffer;
        public uint RowRoot;
        public uint RootM;
        public uint P;
        public ulong Pm;
        public bool ApplyTwiddleAfter;
        public int MaxBlocks;
        public int CurrentL;
    }

    public struct NttFusedMiddleSquareContext
    {
        public uint[] InputBuffer;
        public uint[] OutputBuffer;
        public uint RowForwardRoot;
        public uint InverseRootM;
        public uint P;
        public ulong Pm;
        public bool ApplyTwiddleAfter;
        public int MaxBlocks;
        public int CurrentL;
    }

    private struct TwiddleAfterState
    {
        public bool Enabled;
        public uint WRow;
        public uint WRowStep;
        public uint WHalfL;
        public uint WHalfLStep;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratePowersBlock(
        uint wBase, int tid, int bDim, NttPrimeModulus modulus,
        out uint outTw0, out uint outWBdim)
    {
        int laneId = tid & 31;
        int warpId = tid >> 5;
        int numWarps = bDim >> 5;

        uint v1 = laneId == 0 ? 1u : wBase;
        for (int d = 1; d < 32; d <<= 1)
        {
            uint prev = Warp.ShuffleUpSync(0xFFFFFFFFu, v1, (uint)d);
            if (laneId >= d)
            {
                v1 = ModMul(v1, prev, modulus);
            }
        }

        uint w32 = ModMul(Warp.ShuffleSync(0xFFFFFFFFu, v1, 31), wBase, modulus);

        uint v32 = laneId == 0 ? 1u : w32;
        for (int d = 1; d < 32; d <<= 1)
        {
            uint prev = Warp.ShuffleUpSync(0xFFFFFFFFu, v32, (uint)d);
            if (laneId >= d)
            {
                v32 = ModMul(v32, prev, modulus);
            }
        }

        uint warpFactor = Warp.ShuffleSync(0xFFFFFFFFu, v32, warpId);
        outTw0 = ModMul(warpFactor, v1, modulus);

        uint lastWarpBase = Warp.ShuffleSync(0xFFFFFFFFu, v32, numWarps - 1);
        outWBdim = ModMul(lastWarpBase, w32, modulus);
    }

    [GpuDeviceFunction]
    private static void InitStagePackedTwiddles(
        SharedMemory<uint> sRow, int twOffset, int currentLength, int maxLog2Length,
        uint rowRoot, NttPrimeModulus modulus)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int halfL = currentLength >> 1;

        GeneratePowersBlock(rowRoot, tid, bDim, modulus, out uint twCur, out uint wStep);

        for (int e = tid; e < halfL; e += bDim)
        {
            sRow[twOffset + halfL + e] = twCur;
            twCur = ModMul(twCur, wStep, modulus);
        }

        Gpu.SyncThreads();

        for (int m = tid + 1; m < halfL; m += bDim)
        {
            int s = 31 - (int)Gpu.CountLeadingZeros((uint)m);
            int e = m - (1 << s);
            int scale = (maxLog2Length - 1) - s;
            sRow[twOffset + m] = sRow[twOffset + halfL + (e << scale)];
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlipStagePackedTwiddlesInPlace(
        SharedMemory<uint> sRow, int twOffset, int currentLength, uint primeModulus)
    {
        for (int m = ThreadIndex.X + 2; m < currentLength; m += BlockDimension.X)
        {
            int s = 31 - (int)Gpu.CountLeadingZeros((uint)m);
            int h = 1 << s;
            if (m > h)
            {
                int partner = (3 << s) - m;
                if (m < partner)
                {
                    int idxA = twOffset + m;
                    int idxB = twOffset + partner;
                    uint valA = sRow[idxA];
                    uint valB = sRow[idxB];
                    sRow[idxA] = primeModulus - valB;
                    sRow[idxB] = primeModulus - valA;
                }
                else if (m == partner)
                {
                    int idxMid = twOffset + m;
                    sRow[idxMid] = primeModulus - sRow[idxMid];
                }
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteNttTablelessLoad(
        SharedMemory<uint> sRow, int rowIndex, int currentLength, int maxLog2Length, uint[] deviceData)
    {
        int halfL = currentLength >> 1;
        int blockOffset = rowIndex * currentLength;
        for (int threadId = ThreadIndex.X; threadId < halfL; threadId += BlockDimension.X)
        {
            int idx2 = threadId + halfL;
            uint value1 = deviceData[blockOffset + threadId];
            uint value2 = deviceData[blockOffset + idx2];

            uint rev1 = Gpu.BitReverse((uint)threadId) >> (32 - maxLog2Length);
            uint rev2 = Gpu.BitReverse((uint)idx2) >> (32 - maxLog2Length);

            sRow[(int)(rev1 + (rev1 >> 5))] = value1;
            sRow[(int)(rev2 + (rev2 >> 5))] = value2;
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteNttTablelessButterflyStep(
        SharedMemory<uint> sRow, int step, int threadId, int twOffset, NttPrimeModulus modulus)
    {
        int halfLength = 1 << step;
        int blockIndex = threadId >> step;
        int elementIndex = threadId & (halfLength - 1);
        int position = (blockIndex << (step + 1)) + elementIndex;

        int padPos = position + (position >> 5);
        int posHalf = position + halfLength;
        int padPosHalf = posHalf + (posHalf >> 5);

        uint leftTerm = sRow[padPos];
        uint rightTermSource = sRow[padPosHalf];

        uint rightTerm = step == 0
            ? rightTermSource
            : ModMul(rightTermSource, sRow[twOffset + halfLength + elementIndex], modulus);

        sRow[padPos] = ModAdd(leftTerm, rightTerm, modulus.P);
        sRow[padPosHalf] = ModSub(leftTerm, rightTerm, modulus.P);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RunButterflyStages(
        SharedMemory<uint> sRow, int currentLength, int maxLog2Length, int twOffset, NttPrimeModulus modulus)
    {
        int threadsNeeded = currentLength >> 1;
        for (int step = 0; step < maxLog2Length; step++)
        {
            for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
            {
                ExecuteNttTablelessButterflyStep(sRow, step, threadId, twOffset, modulus);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SquareAndBitReverseInPlace(
        SharedMemory<uint> sRow, int currentLength, int maxLog2Length, NttPrimeModulus modulus)
    {
        for (int k = ThreadIndex.X; k < currentLength; k += BlockDimension.X)
        {
            int revK = (int)(Gpu.BitReverse((uint)k) >> (32 - maxLog2Length));
            if (k == revK)
            {
                int padK = k + (k >> 5);
                uint val = sRow[padK];
                sRow[padK] = ModMul(val, val, modulus);
            }
            else if (k < revK)
            {
                int padK = k + (k >> 5);
                int padRev = revK + (revK >> 5);
                uint valK = sRow[padK];
                uint valRev = sRow[padRev];
                sRow[padRev] = ModMul(valK, valK, modulus);
                sRow[padK] = ModMul(valRev, valRev, modulus);
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RepeatedSquare(uint value, int count, NttPrimeModulus modulus)
    {
        uint acc = value;
        for (int s = 0; s < count; s++)
        {
            acc = ModMul(acc, acc, modulus);
        }

        return acc;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TwiddleAfterState InitTwiddleAfter(
        bool enabled, uint rootM, int maxLog2Length, NttPrimeModulus modulus)
    {
        if (!enabled)
        {
            return new TwiddleAfterState
            {
                Enabled = false,
                WRow = 1u,
                WRowStep = 1u,
                WHalfL = 1u,
                WHalfLStep = 1u
            };
        }

        uint wRow = ModPow(rootM, (uint)BlockIndex.X, modulus);
        uint wRowStep = ModPow(rootM, (uint)GridDimension.X, modulus);
        return new TwiddleAfterState
        {
            Enabled = true,
            WRow = wRow,
            WRowStep = wRowStep,
            WHalfL = RepeatedSquare(wRow, maxLog2Length - 1, modulus),
            WHalfLStep = RepeatedSquare(wRowStep, maxLog2Length - 1, modulus)
        };
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StepTwiddleAfter(ref TwiddleAfterState twiddleAfter, NttPrimeModulus modulus)
    {
        if (twiddleAfter.Enabled)
        {
            twiddleAfter.WRow = ModMul(twiddleAfter.WRow, twiddleAfter.WRowStep, modulus);
            twiddleAfter.WHalfL = ModMul(twiddleAfter.WHalfL, twiddleAfter.WHalfLStep, modulus);
        }
    }

    [GpuDeviceFunction]
    private static void ExecuteNttTablelessTwiddleAfterAndStore(
        SharedMemory<uint> sRow, int rowIndex, int currentLength,
        TwiddleAfterState twiddleAfter, NttPrimeModulus modulus, uint[] deviceOutput)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int halfL = currentLength >> 1;
        int blockOffset = rowIndex * currentLength;

        if (!twiddleAfter.Enabled)
        {
            for (int threadId = tid; threadId < halfL; threadId += bDim)
            {
                int padId1 = threadId + (threadId >> 5);
                int idx2 = threadId + halfL;
                int padId2 = idx2 + (idx2 >> 5);

                deviceOutput[blockOffset + threadId] = sRow[padId1];
                deviceOutput[blockOffset + idx2] = sRow[padId2];
            }

            return;
        }

        GeneratePowersBlock(twiddleAfter.WRow, tid, bDim, modulus, out uint twCur, out uint wStep);

        for (int threadId = tid; threadId < halfL; threadId += bDim)
        {
            int padId1 = threadId + (threadId >> 5);
            int idx2 = threadId + halfL;
            int padId2 = idx2 + (idx2 >> 5);

            uint out1 = sRow[padId1];
            uint out2 = sRow[padId2];

            uint tw2 = ModMul(twCur, twiddleAfter.WHalfL, modulus);
            deviceOutput[blockOffset + threadId] = ModMul(out1, twCur, modulus);
            deviceOutput[blockOffset + idx2] = ModMul(out2, tw2, modulus);

            twCur = ModMul(twCur, wStep, modulus);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteNttTablelessSharedRow(NttTablelessRowContext args)
    {
        var modulus = new NttPrimeModulus { P = args.P, Pm = args.Pm };
        int maxLog2Length = 31 - (int)Gpu.CountLeadingZeros((uint)args.CurrentL);
        int twOffset = args.CurrentL + (args.CurrentL >> 5);
        var sRow = new SharedMemory<uint>(twOffset + args.CurrentL);

        InitStagePackedTwiddles(sRow, twOffset, args.CurrentL, maxLog2Length, args.RowRoot, modulus);
        var twiddleAfter = InitTwiddleAfter(args.ApplyTwiddleAfter, args.RootM, maxLog2Length, modulus);

        for (int rowIndex = BlockIndex.X; rowIndex < args.MaxBlocks; rowIndex += GridDimension.X)
        {
            ExecuteNttTablelessLoad(sRow, rowIndex, args.CurrentL, maxLog2Length, args.InputBuffer);
            Gpu.SyncThreads();

            RunButterflyStages(sRow, args.CurrentL, maxLog2Length, twOffset, modulus);

            ExecuteNttTablelessTwiddleAfterAndStore(sRow, rowIndex, args.CurrentL, twiddleAfter, modulus,
                args.OutputBuffer);
            StepTwiddleAfter(ref twiddleAfter, modulus);
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteNttFusedMiddleSquareRow(NttFusedMiddleSquareContext args)
    {
        var modulus = new NttPrimeModulus { P = args.P, Pm = args.Pm };
        int maxLog2Length = 31 - (int)Gpu.CountLeadingZeros((uint)args.CurrentL);
        int twOffset = args.CurrentL + (args.CurrentL >> 5);
        var sRow = new SharedMemory<uint>(twOffset + args.CurrentL);

        InitStagePackedTwiddles(sRow, twOffset, args.CurrentL, maxLog2Length, args.RowForwardRoot, modulus);
        var twiddleAfter = InitTwiddleAfter(args.ApplyTwiddleAfter, args.InverseRootM, maxLog2Length, modulus);

        for (int rowIndex = BlockIndex.X; rowIndex < args.MaxBlocks; rowIndex += GridDimension.X)
        {
            ExecuteNttTablelessLoad(sRow, rowIndex, args.CurrentL, maxLog2Length, args.InputBuffer);
            Gpu.SyncThreads();

            RunButterflyStages(sRow, args.CurrentL, maxLog2Length, twOffset, modulus);

            SquareAndBitReverseInPlace(sRow, args.CurrentL, maxLog2Length, modulus);
            FlipStagePackedTwiddlesInPlace(sRow, twOffset, args.CurrentL, modulus.P);
            Gpu.SyncThreads();

            RunButterflyStages(sRow, args.CurrentL, maxLog2Length, twOffset, modulus);

            ExecuteNttTablelessTwiddleAfterAndStore(sRow, rowIndex, args.CurrentL, twiddleAfter, modulus,
                args.OutputBuffer);
            StepTwiddleAfter(ref twiddleAfter, modulus);

            if (rowIndex + GridDimension.X < args.MaxBlocks)
            {
                FlipStagePackedTwiddlesInPlace(sRow, twOffset, args.CurrentL, modulus.P);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void MultiplyNttTablelessSharedRowKernel(NttTablelessRowContext args)
    {
        ExecuteNttTablelessSharedRow(args);
    }

    [GpuKernel]
    public static void MultiplyNttFusedMiddleSquareRowKernel(NttFusedMiddleSquareContext args)
    {
        ExecuteNttFusedMiddleSquareRow(args);
    }
}

