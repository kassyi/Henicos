// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntNttKernels
{
    // NTT Primes and primitive roots
    // We use two 32-bit primes for CRT to support convolution values up to 2^55
    // Primes of the form k * 2^25 + 1 to support NTT sizes up to 2^25 (33 million elements)
    public const uint Prime1 = 4194304001; // 125 * 2^25 + 1
    public const uint NttG1 = 3; // Primitive root

    public const uint Prime2 = 2717908993; // 81 * 2^25 + 1
    public const uint NttG2 = 5; // Primitive root

    // Barrett Reduction Constants
    // M = floor(2^64 / P)
    public const ulong BarrettMagic1 = 4398046510UL; // for P1
    public const ulong BarrettMagic2 = 6787108810UL; // for P2

    public const uint Prime3 = 2113929217u; // 63 * 2^25 + 1
    public const uint NttG3 = 5u; // Primitive root
    public const ulong BarrettMagic3 = 8726282755UL; // for P3

    // Division-free 3-prime Garner CRT constants (M = P1 * P2 * P3 ~ 2^94.28)
    public const uint Prime1InvModPrime2 = 1050101200u;
    public const uint Prime1Prime2InvModPrime3 = 733056103u;
    public const uint Prime1ModPrime3 = 2080374784u; // Prime1 % Prime3
    public const ulong Prime1TimesPrime2 = 11399736563693780993UL; // (ulong)Prime1 * Prime2

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

    public struct Ntt35ResolveCarriesContext
    {
        [GpuReadOnly]
        public uint[] Result1;

        [GpuReadOnly]
        public uint[] Result2;

        [GpuReadOnly]
        public uint[] Result3;

        public uint[] OutputBuffer;
        public uint[] BlockCarries;
        public NttGridContext Grid;
        public int MaxWordCount;
        public int ChunkSize;
        public uint InvM1;
        public uint InvM2;
        public uint InvM3;
    }

    // Phase 2: 64-bit Goldilocks Prime (P = 2^64 - 2^32 + 1) Constants
    public const ulong GoldilocksPrime = 0xFFFFFFFF00000001UL;
    public const ulong GoldilocksEpsilon = 0xFFFFFFFFUL;
    public const ulong GoldilocksTwoAdicRoot = 7277203076849721926UL;

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksAdd(ulong operandA, ulong operandB)
    {
        ulong sum = operandA + operandB;
        ulong carry = sum < operandA ? GoldilocksEpsilon : 0UL;
        ulong res = sum + carry;
        if (res < sum)
        {
            res += GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksSub(ulong operandA, ulong operandB)
    {
        ulong diff = operandA - operandB;
        ulong borrow = operandA < operandB ? GoldilocksEpsilon : 0UL;
        ulong res = diff - borrow;
        if (diff < borrow)
        {
            res -= GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction, GpuInline]
    public static ulong GoldilocksMul(ulong operandA, ulong operandB)
    {
        ulong a0 = operandA & GoldilocksEpsilon;
        ulong a1 = operandA >> 32;
        ulong b0 = operandB & GoldilocksEpsilon;
        ulong b1 = operandB >> 32;

        ulong p00 = a0 * b0;
        ulong p01 = a0 * b1;
        ulong p10 = a1 * b0;
        ulong p11 = a1 * b1;

        ulong mid = (p00 >> 32) + (p01 & GoldilocksEpsilon) + (p10 & GoldilocksEpsilon);
        ulong xLo = (p00 & GoldilocksEpsilon) | (mid << 32);
        ulong xHi = p11 + (p01 >> 32) + (p10 >> 32) + (mid >> 32);

        ulong xHiHi = xHi >> 32;
        ulong xHiLo = xHi & GoldilocksEpsilon;

        ulong t0 = xLo - xHiHi;
        if (xLo < xHiHi)
        {
            t0 -= GoldilocksEpsilon;
        }

        ulong t1 = (xHiLo << 32) - xHiLo;

        ulong res = t0 + t1;
        if (res < t0)
        {
            res += GoldilocksEpsilon;
        }

        if (res >= GoldilocksPrime)
        {
            res -= GoldilocksPrime;
        }

        return res;
    }

    [GpuDeviceFunction]
    public static ulong GoldilocksPow(ulong baseValue, ulong exponent)
    {
        ulong result = 1UL;
        baseValue %= GoldilocksPrime;
        while (exponent > 0UL)
        {
            if ((exponent & 1UL) != 0UL)
            {
                result = GoldilocksMul(result, baseValue);
            }

            baseValue = GoldilocksMul(baseValue, baseValue);
            exponent >>= 1;
        }

        return result;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModAdd(uint operandA, uint operandB, uint primeModulus)
    {
        if (operandA >= primeModulus - operandB)
        {
            return operandA - (primeModulus - operandB);
        }

        return operandA + operandB;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModSub(uint operandA, uint operandB, uint primeModulus)
    {
        var result = operandA - operandB;
        if (operandA < operandB)
        {
            result += primeModulus;
        }

        return result;
    }

    // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- Variables are modified via inline PTX assembly which ReSharper cannot detect
    // ReSharper disable once UselessBinaryOperation -- Variables are modified via inline PTX assembly which ReSharper cannot detect
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMulBarrett(uint operandA, uint operandB, uint primeModulus, ulong barrettMagic)
    {
        ulong product = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({product}) : \"r\"({operandA}), \"r\"({operandB}));");

        ulong quotient = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({quotient}) : \"l\"({product}), \"l\"({barrettMagic}));");

        var remainder = product - quotient * primeModulus;
        if (remainder >= primeModulus)
        {
            remainder -= primeModulus;
        }

        return (uint)remainder;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul1(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime1, BarrettMagic1);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul2(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime2, BarrettMagic2);

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModMul3(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime3, BarrettMagic3);

    // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- Variables are modified via inline PTX assembly
    // ReSharper disable once UselessBinaryOperation -- Variables are modified via inline PTX assembly
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ModReduce64Barrett(ulong value, uint primeModulus, ulong barrettMagic)
    {
        ulong quotient = 0UL;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({quotient}) : \"l\"({value}), \"l\"({barrettMagic}));");

        ulong remainder = value - quotient * primeModulus;
        if (remainder >= primeModulus)
        {
            remainder -= primeModulus;
        }

        return (uint)remainder;
    }

    [GpuDeviceFunction]
    public static uint ModPow(uint baseValue, uint exponent, uint primeModulus, ulong barrettMagic)
    {
        uint result = 1;
        baseValue = baseValue % primeModulus;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = ModMulBarrett(result, baseValue, primeModulus, barrettMagic);
            }

            baseValue = ModMulBarrett(baseValue, baseValue, primeModulus, barrettMagic);
            exponent >>= 1;
        }

        return result;
    }


    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratePowersBlock(
        uint wBase, int tid, int bDim, uint primeModulus, ulong barrettMagic,
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
                v1 = ModMulBarrett(v1, prev, primeModulus, barrettMagic);
            }
        }

        uint w32 = ModMulBarrett(Warp.ShuffleSync(0xFFFFFFFFu, v1, 31), wBase, primeModulus, barrettMagic);

        uint v32 = laneId == 0 ? 1u : w32;
        for (int d = 1; d < 32; d <<= 1)
        {
            uint prev = Warp.ShuffleUpSync(0xFFFFFFFFu, v32, (uint)d);
            if (laneId >= d)
            {
                v32 = ModMulBarrett(v32, prev, primeModulus, barrettMagic);
            }
        }

        uint warpFactor = Warp.ShuffleSync(0xFFFFFFFFu, v32, warpId);
        outTw0 = ModMulBarrett(warpFactor, v1, primeModulus, barrettMagic);

        uint lastWarpBase = Warp.ShuffleSync(0xFFFFFFFFu, v32, numWarps - 1);
        outWBdim = ModMulBarrett(lastWarpBase, w32, primeModulus, barrettMagic);
    }

    [GpuDeviceFunction]
    private static void InitStagePackedTwiddles(
        int twOffset, int currentLength, int maxLog2Length,
        uint rowRoot, uint primeModulus, ulong barrettMagic)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int halfL = currentLength >> 1;

        GeneratePowersBlock(rowRoot, tid, bDim, primeModulus, barrettMagic, out uint twCur, out uint wStep);

        for (int e = tid; e < halfL; e += bDim)
        {
            uint idx = (uint)(twOffset + halfL + e);
            Gpu.InlinePtx($"s_row[{idx}] = {twCur};");
            twCur = ModMulBarrett(twCur, wStep, primeModulus, barrettMagic);
        }

        Gpu.SyncThreads();

        for (int m = tid + 1; m < halfL; m += bDim)
        {
            int s = 31 - (int)Gpu.CountLeadingZeros((uint)m);
            int e = m - (1 << s);
            int scale = (maxLog2Length - 1) - s;
            uint srcIdx = (uint)(twOffset + halfL + (e << scale));
            uint dstIdx = (uint)(twOffset + m);
            uint val = 0u;
            Gpu.InlinePtx($"{val} = s_row[{srcIdx}];");
            Gpu.InlinePtx($"s_row[{dstIdx}] = {val};");
        }

        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlipStagePackedTwiddlesInPlace(int twOffset, int currentLength, uint primeModulus)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        for (int m = ThreadIndex.X + 2; m < currentLength; m += BlockDimension.X)
        {
            int s = 31 - (int)Gpu.CountLeadingZeros((uint)m);
            int h = 1 << s;
            if (m > h)
            {
                int partner = (3 << s) - m;
                if (m < partner)
                {
                    uint idxA = (uint)(twOffset + m);
                    uint idxB = (uint)(twOffset + partner);
                    uint valA = 0u;
                    uint valB = 0u;
                    Gpu.InlinePtx($"{valA} = s_row[{idxA}];");
                    Gpu.InlinePtx($"{valB} = s_row[{idxB}];");
                    uint negB = primeModulus - valB;
                    uint negA = primeModulus - valA;
                    Gpu.InlinePtx($"s_row[{idxA}] = {negB};");
                    Gpu.InlinePtx($"s_row[{idxB}] = {negA};");
                }
                else if (m == partner)
                {
                    uint idxMid = (uint)(twOffset + m);
                    uint valMid = 0u;
                    Gpu.InlinePtx($"{valMid} = s_row[{idxMid}];");
                    uint negMid = primeModulus - valMid;
                    Gpu.InlinePtx($"s_row[{idxMid}] = {negMid};");
                }
            }
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteNttTablelessLoad(
        int rowIndex, int currentLength, int maxLog2Length, uint[] deviceData)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        int halfL = currentLength >> 1;
        int blockOffset = rowIndex * currentLength;
        for (int threadId = ThreadIndex.X; threadId < halfL; threadId += BlockDimension.X)
        {
            int idx2 = threadId + halfL;
            uint value1 = deviceData[blockOffset + threadId];
            uint value2 = deviceData[blockOffset + idx2];

            uint rev1 = Gpu.BitReverse((uint)threadId) >> (32 - maxLog2Length);
            uint rev2 = Gpu.BitReverse((uint)idx2) >> (32 - maxLog2Length);

            uint pad1 = rev1 + (rev1 >> 5);
            uint pad2 = rev2 + (rev2 >> 5);

            Gpu.InlinePtx($"s_row[{pad1}] = {value1};");
            Gpu.InlinePtx($"s_row[{pad2}] = {value2};");
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteNttTablelessButterflyStep(
        int step, int threadId, int twOffset,
        uint primeModulus, ulong barrettMagic)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        int halfLength = 1 << step;
        int blockIndex = threadId >> step;
        int elementIndex = threadId & (halfLength - 1);
        int position = (blockIndex << (step + 1)) + elementIndex;

        uint padPos = (uint)(position + (position >> 5));
        int posHalf = position + halfLength;
        uint padPosHalf = (uint)(posHalf + (posHalf >> 5));

        uint leftTerm = 0u;
        Gpu.InlinePtx($"{leftTerm} = s_row[{padPos}];");

        uint rightTermSource = 0u;
        Gpu.InlinePtx($"{rightTermSource} = s_row[{padPosHalf}];");

        uint rightTerm;
        if (step == 0)
        {
            rightTerm = rightTermSource;
        }
        else
        {
            uint twIdx = (uint)(twOffset + halfLength + elementIndex);
            uint twiddleFactor = 0u;
            Gpu.InlinePtx($"{twiddleFactor} = s_row[{twIdx}];");
            rightTerm = ModMulBarrett(rightTermSource, twiddleFactor, primeModulus, barrettMagic);
        }

        uint additionResult = ModAdd(leftTerm, rightTerm, primeModulus);
        uint subtractionResult = ModSub(leftTerm, rightTerm, primeModulus);

        Gpu.InlinePtx($"s_row[{padPos}] = {additionResult};");
        Gpu.InlinePtx($"s_row[{padPosHalf}] = {subtractionResult};");
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RepeatedSquare(uint value, int count, uint primeModulus, ulong barrettMagic)
    {
        uint acc = value;
        for (int s = 0; s < count; s++)
        {
            acc = ModMulBarrett(acc, acc, primeModulus, barrettMagic);
        }

        return acc;
    }

    [GpuDeviceFunction]
    private static void ExecuteNttTablelessTwiddleAfterAndStore(
        int rowIndex, int currentLength,
        bool applyTwiddleAfter, uint wRow, uint wHalfL, uint primeModulus, ulong barrettMagic,
        uint[] deviceOutput)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int halfL = currentLength >> 1;
        int blockOffset = rowIndex * currentLength;

        if (!applyTwiddleAfter)
        {
            for (int threadId = tid; threadId < halfL; threadId += bDim)
            {
                uint padId1 = (uint)(threadId + (threadId >> 5));
                int idx2 = threadId + halfL;
                uint padId2 = (uint)(idx2 + (idx2 >> 5));

                uint out1 = 0u;
                uint out2 = 0u;
                Gpu.InlinePtx($"{out1} = s_row[{padId1}];");
                Gpu.InlinePtx($"{out2} = s_row[{padId2}];");

                deviceOutput[blockOffset + threadId] = out1;
                deviceOutput[blockOffset + idx2] = out2;
            }

            return;
        }

        GeneratePowersBlock(wRow, tid, bDim, primeModulus, barrettMagic, out uint twCur, out uint wStep);

        for (int threadId = tid; threadId < halfL; threadId += bDim)
        {
            uint padId1 = (uint)(threadId + (threadId >> 5));
            int idx2 = threadId + halfL;
            uint padId2 = (uint)(idx2 + (idx2 >> 5));

            uint out1 = 0u;
            uint out2 = 0u;
            Gpu.InlinePtx($"{out1} = s_row[{padId1}];");
            Gpu.InlinePtx($"{out2} = s_row[{padId2}];");

            uint tw2 = ModMulBarrett(twCur, wHalfL, primeModulus, barrettMagic);
            out1 = ModMulBarrett(out1, twCur, primeModulus, barrettMagic);
            out2 = ModMulBarrett(out2, tw2, primeModulus, barrettMagic);

            deviceOutput[blockOffset + threadId] = out1;
            deviceOutput[blockOffset + idx2] = out2;

            twCur = ModMulBarrett(twCur, wStep, primeModulus, barrettMagic);
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteNttTablelessSharedRow(NttTablelessRowContext args)
    {
        var deviceInput = args.InputBuffer;
        var deviceOutput = args.OutputBuffer;
        int currentLength = args.CurrentL;
        uint primeModulus = args.P;
        ulong barrettMagic = args.Pm;
        bool applyTwiddleAfter = args.ApplyTwiddleAfter;
        int maxBlocks = args.MaxBlocks;
        int threadsNeeded = currentLength >> 1;
        int maxLog2Length = 31 - (int)Gpu.CountLeadingZeros((uint)currentLength);
        int twOffset = currentLength + (currentLength >> 5);

        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");

        InitStagePackedTwiddles(twOffset, currentLength, maxLog2Length, args.RowRoot, primeModulus, barrettMagic);

        uint wRow = 1u;
        uint wRowStep = 1u;
        uint wHalfL = 1u;
        uint wHalfLStep = 1u;
        if (applyTwiddleAfter)
        {
            wRow = ModPow(args.RootM, (uint)BlockIndex.X, primeModulus, barrettMagic);
            wRowStep = ModPow(args.RootM, (uint)GridDimension.X, primeModulus, barrettMagic);
            wHalfL = RepeatedSquare(wRow, maxLog2Length - 1, primeModulus, barrettMagic);
            wHalfLStep = RepeatedSquare(wRowStep, maxLog2Length - 1, primeModulus, barrettMagic);
        }

        for (int rowIndex = BlockIndex.X; rowIndex < maxBlocks; rowIndex += GridDimension.X)
        {
            ExecuteNttTablelessLoad(rowIndex, currentLength, maxLog2Length, deviceInput);
            Gpu.SyncThreads();

            for (int step = 0; step < maxLog2Length; step++)
            {
                for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
                {
                    ExecuteNttTablelessButterflyStep(step, threadId, twOffset, primeModulus, barrettMagic);
                }

                Gpu.SyncThreads();
            }

            ExecuteNttTablelessTwiddleAfterAndStore(rowIndex, currentLength, applyTwiddleAfter,
                wRow, wHalfL, primeModulus, barrettMagic, deviceOutput);

            if (applyTwiddleAfter)
            {
                wRow = ModMulBarrett(wRow, wRowStep, primeModulus, barrettMagic);
                wHalfL = ModMulBarrett(wHalfL, wHalfLStep, primeModulus, barrettMagic);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteNttFusedMiddleSquareRow(NttFusedMiddleSquareContext args)
    {
        var deviceInput = args.InputBuffer;
        var deviceOutput = args.OutputBuffer;
        int currentLength = args.CurrentL;
        uint primeModulus = args.P;
        ulong barrettMagic = args.Pm;
        bool applyTwiddleAfter = args.ApplyTwiddleAfter;
        int maxBlocks = args.MaxBlocks;
        int threadsNeeded = currentLength >> 1;
        int maxLog2Length = 31 - (int)Gpu.CountLeadingZeros((uint)currentLength);
        int twOffset = currentLength + (currentLength >> 5);

        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");

        InitStagePackedTwiddles(twOffset, currentLength, maxLog2Length, args.RowForwardRoot, primeModulus,
            barrettMagic);

        uint wRow = 1u;
        uint wRowStep = 1u;
        uint wHalfL = 1u;
        uint wHalfLStep = 1u;
        if (applyTwiddleAfter)
        {
            wRow = ModPow(args.InverseRootM, (uint)BlockIndex.X, primeModulus, barrettMagic);
            wRowStep = ModPow(args.InverseRootM, (uint)GridDimension.X, primeModulus, barrettMagic);
            wHalfL = RepeatedSquare(wRow, maxLog2Length - 1, primeModulus, barrettMagic);
            wHalfLStep = RepeatedSquare(wRowStep, maxLog2Length - 1, primeModulus, barrettMagic);
        }

        for (int rowIndex = BlockIndex.X; rowIndex < maxBlocks; rowIndex += GridDimension.X)
        {
            ExecuteNttTablelessLoad(rowIndex, currentLength, maxLog2Length, deviceInput);
            Gpu.SyncThreads();

            for (int step = 0; step < maxLog2Length; step++)
            {
                for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
                {
                    ExecuteNttTablelessButterflyStep(step, threadId, twOffset, primeModulus, barrettMagic);
                }

                Gpu.SyncThreads();
            }

            // Pointwise square fused with in-shared bit-reversal AND in-place stage-twiddle inversion.
            for (int k = ThreadIndex.X; k < currentLength; k += BlockDimension.X)
            {
                int revK = (int)(Gpu.BitReverse((uint)k) >> (32 - maxLog2Length));
                if (k == revK)
                {
                    uint padK = (uint)(k + (k >> 5));
                    uint val = 0u;
                    Gpu.InlinePtx($"{val} = s_row[{padK}];");
                    uint sq = ModMulBarrett(val, val, primeModulus, barrettMagic);
                    Gpu.InlinePtx($"s_row[{padK}] = {sq};");
                }
                else if (k < revK)
                {
                    uint padK = (uint)(k + (k >> 5));
                    uint padRev = (uint)(revK + (revK >> 5));
                    uint valK = 0u;
                    uint valRev = 0u;
                    Gpu.InlinePtx($"{valK} = s_row[{padK}];");
                    Gpu.InlinePtx($"{valRev} = s_row[{padRev}];");
                    uint sqK = ModMulBarrett(valK, valK, primeModulus, barrettMagic);
                    uint sqRev = ModMulBarrett(valRev, valRev, primeModulus, barrettMagic);
                    Gpu.InlinePtx($"s_row[{padRev}] = {sqK};");
                    Gpu.InlinePtx($"s_row[{padK}] = {sqRev};");
                }
            }

            FlipStagePackedTwiddlesInPlace(twOffset, currentLength, primeModulus);
            Gpu.SyncThreads();

            for (int step = 0; step < maxLog2Length; step++)
            {
                for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
                {
                    ExecuteNttTablelessButterflyStep(step, threadId, twOffset, primeModulus, barrettMagic);
                }

                Gpu.SyncThreads();
            }

            ExecuteNttTablelessTwiddleAfterAndStore(rowIndex, currentLength, applyTwiddleAfter,
                wRow, wHalfL, primeModulus, barrettMagic, deviceOutput);

            if (applyTwiddleAfter)
            {
                wRow = ModMulBarrett(wRow, wRowStep, primeModulus, barrettMagic);
                wHalfL = ModMulBarrett(wHalfL, wHalfLStep, primeModulus, barrettMagic);
            }

            // Restore forward stage-packed twiddles for the next rowIndex iteration in the grid-stride loop.
            if (rowIndex + GridDimension.X < maxBlocks)
            {
                FlipStagePackedTwiddlesInPlace(twOffset, currentLength, primeModulus);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    public static void ExecuteNttSharedRow(NumberTheoreticTransform args)
    {
        var deviceData = args.Buffer;
        var deviceOutput = args.TemporaryBuffer;
        var deviceTwiddle = args.Twiddle;
        var transformLength = args.Grid.M;
        var currentLength = args.CurrentL;
        var primeModulus = args.P;
        var barrettMagic = args.Pm;
        var applyTwiddleBefore = args.ApplyTwiddleBefore;
        var applyTwiddleAfter = args.ApplyTwiddleAfter;
        var maxBlocks = args.MaxBlocks;
        var threadsNeeded = currentLength / 2;

        int maxLog2Length = 0;
        while ((1 << maxLog2Length) < currentLength)
        {
            maxLog2Length++;
        }

        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");

        for (int rowIndex = BlockIndex.X; rowIndex < maxBlocks; rowIndex += GridDimension.X)
        {
            for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
            {
                ExecuteNttLoadAndTwiddleBefore(rowIndex, threadId, currentLength, maxLog2Length, transformLength,
                    applyTwiddleBefore, primeModulus, barrettMagic, deviceData, deviceTwiddle);
            }

            Gpu.SyncThreads();

            for (int step = 0; step < maxLog2Length; step++)
            {
                for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
                {
                    ExecuteNttButterflyStep(step, threadId, transformLength, primeModulus, barrettMagic,
                        deviceTwiddle);
                }

                Gpu.SyncThreads();
            }

            for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
            {
                ExecuteNttTwiddleAfterAndStore(rowIndex, threadId, currentLength, transformLength, applyTwiddleAfter,
                    primeModulus, barrettMagic, deviceOutput, deviceTwiddle);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    private static void ExecuteNttLoadAndTwiddleBefore(
        int rowIndex, int threadId, int currentLength, int maxLog2Length, int transformLength,
        bool applyTwiddleBefore, uint primeModulus, ulong barrettMagic,
        [GpuReadOnly] uint[] deviceData, [GpuReadOnly] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var blockOffset = rowIndex * currentLength;
        var value1 = deviceData[blockOffset + threadId];
        var value2 = deviceData[blockOffset + threadId + currentLength / 2];

        uint reversedThreadId1 = Gpu.BitReverse((uint)threadId) >> (32 - maxLog2Length);
        uint reversedThreadId2 = Gpu.BitReverse((uint)(threadId + currentLength / 2)) >> (32 - maxLog2Length);

        if (applyTwiddleBefore)
        {
            ulong transformMask = (ulong)(transformLength - 1);
            ulong twiddleIndex1 = ((ulong)rowIndex * (ulong)threadId) & transformMask;
            value1 = ModMulBarrett(value1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);

            ulong twiddleIndex2 = ((ulong)rowIndex * (ulong)(threadId + (currentLength >> 1))) & transformMask;
            value2 = ModMulBarrett(value2, deviceTwiddle[twiddleIndex2], primeModulus, barrettMagic);
        }

        uint pad1 = reversedThreadId1 + (reversedThreadId1 >> 5);
        uint pad2 = reversedThreadId2 + (reversedThreadId2 >> 5);

        Gpu.InlinePtx($"s_row[{pad1}] = {value1};");
        Gpu.InlinePtx($"s_row[{pad2}] = {value2};");
    }

    [GpuDeviceFunction]
    private static void ExecuteNttButterflyStep(
        int step, int threadId, int transformLength,
        uint primeModulus, ulong barrettMagic,
        [GpuReadOnly] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        int halfLength = 1 << step;
        int blockIndex = threadId >> step;
        int elementIndex = threadId & (halfLength - 1);
        int position = (blockIndex << (step + 1)) + elementIndex;

        int twiddleFactorIndex = elementIndex * (transformLength >> (step + 1));
        uint twiddleFactor = deviceTwiddle[twiddleFactorIndex];

        uint padPos = (uint)(position + (position >> 5));
        uint padPosHalf = (uint)((position + halfLength) + ((position + halfLength) >> 5));

        uint leftTerm = 0;
        Gpu.InlinePtx($"{leftTerm} = s_row[{padPos}];");

        uint rightTermSource = 0;
        Gpu.InlinePtx($"{rightTermSource} = s_row[{padPosHalf}];");
        var rightTerm = ModMulBarrett(rightTermSource, twiddleFactor, primeModulus, barrettMagic);

        var additionResult = ModAdd(leftTerm, rightTerm, primeModulus);
        var subtractionResult = ModSub(leftTerm, rightTerm, primeModulus);

        Gpu.InlinePtx($"s_row[{padPos}] = {additionResult};");
        Gpu.InlinePtx($"s_row[{padPosHalf}] = {subtractionResult};");
    }

    [GpuDeviceFunction]
    private static void ExecuteNttTwiddleAfterAndStore(
        int rowIndex, int threadId, int currentLength, int transformLength,
        bool applyTwiddleAfter, uint primeModulus, ulong barrettMagic,
        uint[] deviceOutput, [GpuReadOnly] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var blockOffset = rowIndex * currentLength;

        uint padId1 = (uint)(threadId + (threadId >> 5));
        uint padId2 = (uint)((threadId + currentLength / 2) + ((threadId + currentLength / 2) >> 5));

        uint output1 = 0;
        uint output2 = 0;
        Gpu.InlinePtx($"{output1} = s_row[{padId1}];");
        Gpu.InlinePtx($"{output2} = s_row[{padId2}];");

        if (applyTwiddleAfter)
        {
            ulong transformMask = (ulong)(transformLength - 1);
            ulong twiddleIndex1 = ((ulong)rowIndex * (ulong)threadId) & transformMask;
            output1 = ModMulBarrett(output1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);

            ulong twiddleIndex2 = ((ulong)rowIndex * (ulong)(threadId + (currentLength >> 1))) & transformMask;
            output2 = ModMulBarrett(output2, deviceTwiddle[twiddleIndex2], primeModulus, barrettMagic);
        }

        deviceOutput[blockOffset + threadId] = output1;
        deviceOutput[blockOffset + threadId + currentLength / 2] = output2;
    }

    [GpuDeviceFunction]
    public static void ExecuteNttPointwiseSquare(uint[] deviceData1, uint[] deviceData2, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1[index] = ModMulBarrett(deviceData1[index], deviceData1[index], Prime1, BarrettMagic1);
            deviceData2[index] = ModMulBarrett(deviceData2[index], deviceData2[index], Prime2, BarrettMagic2);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteNttPointwiseMultiply(uint[] deviceData1A, uint[] deviceData2A, uint[] deviceData1B,
        uint[] deviceData2B, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1A[index] = ModMulBarrett(deviceData1A[index], deviceData1B[index], Prime1, BarrettMagic1);
            deviceData2A[index] = ModMulBarrett(deviceData2A[index], deviceData2B[index], Prime2, BarrettMagic2);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteCrtComputeCoefficients([GpuReadOnly] uint[] deviceResult1,
        [GpuReadOnly] uint[] deviceResult2,
        uint[] uncarriedLow, uint[] uncarriedHigh,
        int transformLength)
    {
        var inverseN1 = ModPow((uint)transformLength, Prime1 - 2, Prime1, BarrettMagic1);
        var inverseN2 = ModPow((uint)transformLength, Prime2 - 2, Prime2, BarrettMagic2);

        Gpu.GridStrideLoop(transformLength, index =>
        {
            const ulong Modulus1 = Prime1;
            const ulong Modulus2 = Prime2;

            ulong value1 = ModMulBarrett(deviceResult1[index], inverseN1, Prime1, BarrettMagic1);
            ulong value2 = ModMulBarrett(deviceResult2[index], inverseN2, Prime2, BarrettMagic2);

            ulong difference;
            ulong v1Mod2 = value1 % Modulus2;
            if (value2 >= v1Mod2)
            {
                difference = value2 - v1Mod2;
            }
            else
            {
                difference = value2 + Modulus2 - v1Mod2;
            }

            const ulong Prime1InverseModPrime2 = 1050101200UL;
            var crtCoefficient = (difference * Prime1InverseModPrime2) % Modulus2;
            var recombinedValue = value1 + Modulus1 * crtCoefficient;

            uncarriedLow[index] = (uint)(recombinedValue & 0xFFFFFFFF);
            uncarriedHigh[index] = (uint)(recombinedValue >> 32);
        });
    }

    [GpuDeviceFunction]
    public static void ExecuteCrtComputeCoefficientsTransposed([GpuReadOnly] uint[] deviceResult1,
        [GpuReadOnly] uint[] deviceResult2,
        uint[] uncarriedLow, uint[] uncarriedHigh,
        int transformLength, int n1, int n2)
    {
        var inverseN1 = ModPow((uint)transformLength, Prime1 - 2, Prime1, BarrettMagic1);
        var inverseN2 = ModPow((uint)transformLength, Prime2 - 2, Prime2, BarrettMagic2);

        int log2N2 = 0;
        if (transformLength > 512)
        {
            while ((1 << log2N2) < n2)
            {
                log2N2++;
            }
        }

        int n2Mask = n2 - 1;

        Gpu.GridStrideLoop(transformLength, index =>
        {
            const ulong Modulus1 = Prime1;
            const ulong Modulus2 = Prime2;

            int srcIdx = transformLength > 512 ? ((index & n2Mask) * n1 + (index >> log2N2)) : index;
            ulong value1 = ModMulBarrett(deviceResult1[srcIdx], inverseN1, Prime1, BarrettMagic1);
            ulong value2 = ModMulBarrett(deviceResult2[srcIdx], inverseN2, Prime2, BarrettMagic2);

            ulong difference;
            ulong v1Mod2 = value1 % Modulus2;
            if (value2 >= v1Mod2)
            {
                difference = value2 - v1Mod2;
            }
            else
            {
                difference = value2 + Modulus2 - v1Mod2;
            }

            const ulong Prime1InverseModPrime2 = 1050101200UL;
            var crtCoefficient = (difference * Prime1InverseModPrime2) % Modulus2;
            var recombinedValue = value1 + Modulus1 * crtCoefficient;

            uncarriedLow[index] = (uint)(recombinedValue & 0xFFFFFFFF);
            uncarriedHigh[index] = (uint)(recombinedValue >> 32);
        });
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeNttLocalSum(uint[] uncarriedLow, uint[] uncarriedHigh, int i, int maxWordCount,
        int transformLength)
    {
        ulong localSum = 0;
        if (i < maxWordCount)
        {
            // Each NTT coefficient c_j (64-bit) has weight 2^(16*j).
            // For word i (weight 2^(32*i)), relevant coefficients are j = 2*i + 1 down to 2*i - 3.
            Gpu.ForUnroll(0, 5, step =>
            {
                int idx = 2 * i + 1 - step;
                if (idx >= 0 && idx < transformLength)
                {
                    ulong c = uncarriedLow[idx] | ((ulong)uncarriedHigh[idx] << 32);
                    if (step == 0)
                    {
                        localSum += (c & 0xFFFFul) << 16;
                    }
                    else
                    {
                        localSum += (c >> ((step - 1) * 16)) & 0xFFFFFFFFul;
                    }
                }
            });
        }

        return localSum;
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesBlocked(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        var tIdx = ThreadIndex.X;
        var bDim = BlockDimension.X;
        var numBlocks = (maxWordCount + chunkSize - 1) / chunkSize;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        for (int bIdx = BlockIndex.X; bIdx < numBlocks; bIdx += GridDimension.X)
        {
            int startIdx = bIdx * chunkSize;
            int i = startIdx + tIdx;

            ulong localSum = ComputeNttLocalSum(uncarriedLow, uncarriedHigh, i, maxWordCount, transformLength);

            uint L = (uint)(localSum & 0xFFFFFFFFul);
            uint H = (uint)(localSum >> 32);

            int laneId = tIdx % 32;
            int warpId = tIdx / 32;

            uint prevH = ResolveOverlapH(H, sharedH, laneId, warpId);

            ulong sum = (ulong)L + prevH;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim, out uint scanG, out uint scanP,
                out uint carryIn);

            if (i < maxWordCount)
            {
                outputBuffer[i] = (uint)sum + carryIn;
            }

            if (tIdx == 0)
            {
                uint lastH = sharedH[((bDim + 31) / 32) - 1];
                uint waveCarry =
                    GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]),
                        0u);
                blockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    private static uint ResolveOverlapH(uint h, SharedMemory<uint> sharedH, int laneId, int warpId)
    {
        uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, h, 1);

        if (laneId == 31)
        {
            sharedH[warpId] = h;
        }

        Gpu.SyncThreads();

        if (laneId == 0)
        {
            if (warpId == 0)
            {
                prevH = 0u;
            }
            else
            {
                prevH = sharedH[warpId - 1];
            }
        }

        return prevH;
    }

    [GpuDeviceFunction]
    private static void ComputeBlockCarryScan(uint g, uint p, SharedMemory<uint> sharedCarry, int laneId, int warpId,
        int bDim, out uint scanG, out uint scanP, out uint carryIn)
    {
        var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), laneId);
        scanG = warpScan.Generate;
        scanP = warpScan.Propagate;

        if (laneId == 31)
        {
            sharedCarry[warpId] = GpuBigIntKernels.PackCarryState(warpScan);
        }

        Gpu.SyncThreads();

        if (warpId == 0)
        {
            int numWarps = (bDim + 31) / 32;
            var valState = laneId < numWarps
                ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId])
                : new GpuBigIntKernels.CarryState(0, 0);
            var blockScan = GpuBigIntKernels.WarpScanGp(valState, laneId);

            if (laneId < numWarps)
            {
                sharedCarry[laneId] = GpuBigIntKernels.PackCarryState(blockScan);
            }
        }

        Gpu.SyncThreads();

        carryIn = 0u;

        if (warpId > 0)
        {
            carryIn = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[warpId - 1]), carryIn);
        }

        uint prevScanG = Warp.ShuffleUpSync(0xFFFFFFFFu, scanG, 1);
        uint prevScanP = Warp.ShuffleUpSync(0xFFFFFFFFu, scanP, 1);
        if (laneId > 0)
        {
            carryIn = prevScanG | (prevScanP & carryIn);
        }
    }

    [GpuKernel]
    public static void ResolveNttCarriesBlockedKernel(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        ExecuteResolveNttCarriesBlocked(uncarriedLow, uncarriedHigh, outputBuffer, blockCarries, transformLength,
            maxWordCount, chunkSize);
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNtt35CarriesBlocked(Ntt35ResolveCarriesContext ctx)
    {
        int tIdx = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int chunkSize = ctx.ChunkSize;
        int maxWordCount = ctx.MaxWordCount;
        int m = ctx.Grid.M;
        int n1 = ctx.Grid.N1;
        int n2 = ctx.Grid.N2;
        int numBlocks = (maxWordCount + chunkSize - 1) / chunkSize;

        uint invN1 = ctx.InvM1;
        uint invN2 = ctx.InvM2;
        uint invN3 = ctx.InvM3;

        int log2N2 = 0;
        if (m > 512)
        {
            while ((1 << log2N2) < n2)
            {
                log2N2++;
            }
        }

        int n2Mask = n2 - 1;

        var sharedCarry = new SharedMemory<uint>(32);
        var sharedH = new SharedMemory<uint>(32);
        // Any 1024-word output chunk spans at most ceil((32736 + 126) / 35) + 1 <= 940 35-bit limbs (< 960).
        var sCrtLo = new SharedMemory<uint>(960);
        var sCrtMid = new SharedMemory<uint>(960);
        var sCrtHi = new SharedMemory<uint>(960);

        for (int bIdx = BlockIndex.X; bIdx < numBlocks; bIdx += GridDimension.X)
        {
            int startWord = bIdx * chunkSize;
            int lastWord = startWord + chunkSize - 1;
            if (lastWord >= maxWordCount)
            {
                lastWord = maxWordCount - 1;
            }

            ulong blockBitStart = (ulong)(uint)startWord << 5;
            ulong blockBitEnd = (ulong)(uint)lastWord << 5;
            int jBlockStart = blockBitStart <= 95UL ? 0 : (int)((blockBitStart - 61UL) / 35UL);
            int jBlockEnd = (int)((blockBitEnd + 31UL) / 35UL);
            if (jBlockEnd >= m)
            {
                jBlockEnd = m - 1;
            }

            int jCount = jBlockEnd >= jBlockStart ? (jBlockEnd - jBlockStart + 1) : 0;

            // Cooperative division-free Garner CRT3 reconstruction into 96-bit shared-memory coefficients.
            for (int localJ = tIdx; localJ < jCount; localJ += bDim)
            {
                int j = jBlockStart + localJ;
                int srcIdx = m > 512 ? ((j & n2Mask) * n1 + (j >> log2N2)) : j;

                uint v1 = ModMulBarrett(ctx.Result1[srcIdx], invN1, Prime1, BarrettMagic1);
                uint v2 = ModMulBarrett(ctx.Result2[srcIdx], invN2, Prime2, BarrettMagic2);
                uint v3 = ModMulBarrett(ctx.Result3[srcIdx], invN3, Prime3, BarrettMagic3);

                uint v1Mod2 = v1 >= Prime2 ? v1 - Prime2 : v1;
                uint diff12 = ModSub(v2, v1Mod2, Prime2);
                uint k12 = ModMulBarrett(diff12, Prime1InvModPrime2, Prime2, BarrettMagic2);
                ulong x12 = (ulong)v1 + (ulong)Prime1 * (ulong)k12;

                uint v1Mod3 = v1 >= Prime3 ? v1 - Prime3 : v1;
                uint k12Mod3 = k12 >= Prime3 ? k12 - Prime3 : k12;
                uint term3 = ModMulBarrett(Prime1ModPrime3, k12Mod3, Prime3, BarrettMagic3);
                uint x12Mod3 = ModAdd(v1Mod3, term3, Prime3);
                uint diff3 = ModSub(v3, x12Mod3, Prime3);
                uint k3 = ModMulBarrett(diff3, Prime1Prime2InvModPrime3, Prime3, BarrettMagic3);

                ulong p1p2Const = Prime1TimesPrime2;
                ulong k3Wide = (ulong)k3;
                ulong prodLo = p1p2Const * k3Wide;
                ulong prodHi = 0UL;
                Gpu.InlinePtx(
                    $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({prodHi}) : \"l\"({p1p2Const}), \"l\"({k3Wide}));");

                ulong cLo = x12 + prodLo;
                uint cHi = (uint)prodHi + (cLo < x12 ? 1u : 0u);

                sCrtLo[localJ] = (uint)(cLo & 0xFFFFFFFFUL);
                sCrtMid[localJ] = (uint)(cLo >> 32);
                sCrtHi[localJ] = cHi;
            }

            Gpu.SyncThreads();

            int i = startWord + tIdx;
            ulong localSum = 0UL;
            if (i < maxWordCount)
            {
                ulong bitStart = (ulong)(uint)i << 5;
                int jMin = bitStart <= 95UL ? 0 : (int)((bitStart - 61UL) / 35UL);
                int jMax = (int)((bitStart + 31UL) / 35UL);
                if (jMax >= m)
                {
                    jMax = m - 1;
                }

                for (int j = jMin; j <= jMax; j++)
                {
                    int localIdx = j - jBlockStart;
                    ulong cLo = sCrtLo[localIdx] | ((ulong)sCrtMid[localIdx] << 32);
                    uint cHi = sCrtHi[localIdx];
                    ulong coeffBit = (ulong)(uint)j * 35UL;

                    uint slice = 0u;
                    if (coeffBit >= bitStart)
                    {
                        int leftShift = (int)(coeffBit - bitStart);
                        slice = ((uint)cLo) << leftShift;
                    }
                    else
                    {
                        int rightShift = (int)(bitStart - coeffBit);
                        if (rightShift < 64)
                        {
                            slice = (uint)((cLo >> rightShift) | ((ulong)cHi << (64 - rightShift)));
                        }
                        else if (rightShift == 64)
                        {
                            slice = cHi;
                        }
                        else
                        {
                            slice = cHi >> (rightShift - 64);
                        }
                    }

                    localSum += slice;
                }
            }

            uint L = (uint)(localSum & 0xFFFFFFFFUL);
            uint H = (uint)(localSum >> 32);

            int laneId = tIdx & 31;
            int warpId = tIdx >> 5;

            uint prevH = ResolveOverlapH(H, sharedH, laneId, warpId);

            ulong sum = (ulong)L + prevH;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim, out uint scanG, out uint scanP,
                out uint carryIn);

            if (i < maxWordCount)
            {
                ctx.OutputBuffer[i] = (uint)sum + carryIn;
            }

            if (tIdx == 0)
            {
                int lastWarp = ((bDim + 31) >> 5) - 1;
                uint lastH = sharedH[lastWarp];
                uint waveCarry = GpuBigIntKernels.ApplyCarry(
                    GpuBigIntKernels.UnpackCarryState(sharedCarry[lastWarp]), 0u);
                ctx.BlockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ResolveNtt35CarriesBlockedKernel(Ntt35ResolveCarriesContext ctx)
    {
        ExecuteResolveNtt35CarriesBlocked(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GarnerCrt3Reconstruct96(
        uint raw1, uint raw2, uint raw3,
        uint invN1, uint invN2, uint invN3,
        out uint outLo, out uint outMid, out uint outHi)
    {
        uint v1 = ModMulBarrett(raw1, invN1, Prime1, BarrettMagic1);
        uint v2 = ModMulBarrett(raw2, invN2, Prime2, BarrettMagic2);
        uint v3 = ModMulBarrett(raw3, invN3, Prime3, BarrettMagic3);

        uint v1Mod2 = v1 >= Prime2 ? v1 - Prime2 : v1;
        uint diff12 = ModSub(v2, v1Mod2, Prime2);
        uint k12 = ModMulBarrett(diff12, Prime1InvModPrime2, Prime2, BarrettMagic2);
        ulong x12 = (ulong)v1 + (ulong)Prime1 * (ulong)k12;

        uint v1Mod3 = v1 >= Prime3 ? v1 - Prime3 : v1;
        uint k12Mod3 = k12 >= Prime3 ? k12 - Prime3 : k12;
        uint term3 = ModMulBarrett(Prime1ModPrime3, k12Mod3, Prime3, BarrettMagic3);
        uint x12Mod3 = ModAdd(v1Mod3, term3, Prime3);
        uint diff3 = ModSub(v3, x12Mod3, Prime3);
        uint k3 = ModMulBarrett(diff3, Prime1Prime2InvModPrime3, Prime3, BarrettMagic3);

        ulong p1p2Const = Prime1TimesPrime2;
        ulong k3Wide = (ulong)k3;
        ulong prodLo = p1p2Const * k3Wide;
        ulong prodHi = 0UL;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({prodHi}) : \"l\"({p1p2Const}), \"l\"({k3Wide}));");

        ulong cLo = x12 + prodLo;
        outHi = (uint)prodHi + (cLo < x12 ? 1u : 0u);
        outLo = (uint)(cLo & 0xFFFFFFFFUL);
        outMid = (uint)(cLo >> 32);
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNtt35CarriesTiled32x32(Ntt35ResolveCarriesContext ctx)
    {
        int tid = ThreadIndex.X;
        int tx = tid & 31;
        int ty = tid >> 5;
        int m = ctx.Grid.M;
        int n1 = ctx.Grid.N1;
        int n2 = ctx.Grid.N2;
        int maxWordCount = ctx.MaxWordCount;

        int tilesX = n2 >> 5;
        int tilesY = n1 >> 5;
        int totalTiles = tilesX * tilesY;
        int tilesXMask = tilesX - 1;
        int log2TilesX = 31 - (int)Gpu.CountLeadingZeros((uint)tilesX);

        uint invN1 = ctx.InvM1;
        uint invN2 = ctx.InvM2;
        uint invN3 = ctx.InvM3;

        var sLo = new SharedMemory<uint>(1120);
        var sMid = new SharedMemory<uint>(1120);
        var sHi = new SharedMemory<uint>(1120);

        for (int tileIdx = BlockIndex.X; tileIdx < totalTiles; tileIdx += GridDimension.X)
        {
            int bx = tileIdx & tilesXMask;
            int by = tileIdx >> log2TilesX;
            int r0 = bx << 5;
            int c0 = by << 5;
            int cIn = c0 + tx;

            int rIn = r0 + ty;
            int srcIdx = rIn * n1 + cIn;
            GarnerCrt3Reconstruct96(
                ctx.Result1[srcIdx], ctx.Result2[srcIdx], ctx.Result3[srcIdx],
                invN1, invN2, invN3,
                out uint loMain, out uint midMain, out uint hiMain);

            int mainSlot = tx * 35 + ty + 2;
            sLo[mainSlot] = loMain;
            sMid[mainSlot] = midMain;
            sHi[mainSlot] = hiMain;

            if (ty < 2)
            {
                uint loPrev = 0u;
                uint midPrev = 0u;
                uint hiPrev = 0u;
                if (r0 > 0)
                {
                    int idxPrev = (r0 - 2 + ty) * n1 + cIn;
                    GarnerCrt3Reconstruct96(
                        ctx.Result1[idxPrev], ctx.Result2[idxPrev], ctx.Result3[idxPrev],
                        invN1, invN2, invN3,
                        out loPrev, out midPrev, out hiPrev);
                }
                else if (cIn > 0)
                {
                    int idxPrev = (n2 - 2 + ty) * n1 + (cIn - 1);
                    GarnerCrt3Reconstruct96(
                        ctx.Result1[idxPrev], ctx.Result2[idxPrev], ctx.Result3[idxPrev],
                        invN1, invN2, invN3,
                        out loPrev, out midPrev, out hiPrev);
                }

                int prevSlot = tx * 35 + ty;
                sLo[prevSlot] = loPrev;
                sMid[prevSlot] = midPrev;
                sHi[prevSlot] = hiPrev;
            }

            Gpu.SyncThreads();

            int segBase = ty * 35;
            int slot0 = segBase + tx + 2;
            int slot1 = segBase + tx + 1;
            int slot2 = segBase + tx;

            ulong lo64Cur = sLo[slot0] | ((ulong)sMid[slot0] << 32);
            ulong part0 = lo64Cur & 0x7FFFFFFFFUL;

            ulong lo64Prev1 = sLo[slot1] | ((ulong)sMid[slot1] << 32);
            uint hiPrev1 = sHi[slot1];
            ulong part1 = ((lo64Prev1 >> 35) | ((ulong)hiPrev1 << 29)) & 0x7FFFFFFFFUL;

            ulong part2 = (ulong)(sHi[slot2] >> 6);

            ulong sum35 = part0 + part1 + part2;
            ulong l35 = sum35 & 0x7FFFFFFFFUL;
            uint h2 = (uint)(sum35 >> 35);

            uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, h2, 1u);
            if (tx == 0)
            {
                prevH = 0u;
            }

            ulong u35 = l35 + prevH;
            uint g = (uint)(u35 >> 35);
            ulong low35 = u35 & 0x7FFFFFFFFUL;
            uint p = low35 == 0x7FFFFFFFFUL ? 1u : 0u;

            var warpScan = GpuBigIntKernels.WarpScanGp(new GpuBigIntKernels.CarryState(g, p), tx);
            uint carryIn = Warp.ShuffleUpSync(0xFFFFFFFFu, warpScan.Generate, 1u);
            if (tx == 0)
            {
                carryIn = 0u;
            }

            ulong finalV = (low35 + carryIn) & 0x7FFFFFFFFUL;
            uint vLo = (uint)(finalV & 0xFFFFFFFFUL);
            uint vHi = (uint)(finalV >> 32);

            int cOut = c0 + ty;
            int tileLinearIdx = cOut * tilesX + bx;
            if (tx == 31)
            {
                ctx.BlockCarries[tileLinearIdx] = h2 + warpScan.Generate;
            }

            int wordBase = tileLinearIdx * 35;

            int bitPos1 = tx << 5;
            int k1 = (bitPos1 * 937) >> 15;
            int off1 = bitPos1 - k1 * 35;
            int k1Next = k1 + 1 < 32 ? k1 + 1 : 31;
            ulong a1 = Warp.ShuffleSync(0xFFFFFFFFu, vLo, k1) | ((ulong)Warp.ShuffleSync(0xFFFFFFFFu, vHi, k1) << 32);
            uint b1Lo = Warp.ShuffleSync(0xFFFFFFFFu, vLo, k1Next);
            uint outWord1 = off1 <= 3 ? (uint)(a1 >> off1) : ((uint)(a1 >> off1) | (b1Lo << (35 - off1)));
            if (wordBase + tx < maxWordCount)
            {
                ctx.OutputBuffer[wordBase + tx] = outWord1;
            }

            int w2 = 32 + (tx < 3 ? tx : 2);
            int bitPos2 = w2 << 5;
            int k2 = (bitPos2 * 937) >> 15;
            int off2 = bitPos2 - k2 * 35;
            int k2Next = k2 + 1 < 32 ? k2 + 1 : 31;
            ulong a2 = Warp.ShuffleSync(0xFFFFFFFFu, vLo, k2) | ((ulong)Warp.ShuffleSync(0xFFFFFFFFu, vHi, k2) << 32);
            uint b2Lo = Warp.ShuffleSync(0xFFFFFFFFu, vLo, k2Next);
            uint outWord2 = off2 <= 3 ? (uint)(a2 >> off2) : ((uint)(a2 >> off2) | (b2Lo << (35 - off2)));
            if (tx < 3 && wordBase + 32 + tx < maxWordCount)
            {
                ctx.OutputBuffer[wordBase + 32 + tx] = outWord2;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ResolveNtt35CarriesTiled32x32Kernel(Ntt35ResolveCarriesContext ctx)
    {
        ExecuteResolveNtt35CarriesTiled32x32(ctx);
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesApply(uint[] outputBuffer, uint[] blockCarries, int maxWordCount,
        int chunkSize, int numBlocks)
    {
        Gpu.GridStrideLoop(numBlocks, globalBlockIdx =>
        {
            var carry = blockCarries[globalBlockIdx];
            if (carry > 0)
            {
                int startIdx = (globalBlockIdx + 1) * chunkSize;
                AtomicAddWithCarry(outputBuffer, startIdx, carry, maxWordCount);
            }
        });
    }

    [GpuKernel]
    public static void ResolveNttCarriesApplyKernel(uint[] outputBuffer, uint[] blockCarries, int maxWordCount,
        int chunkSize, int numBlocks)
    {
        ExecuteResolveNttCarriesApply(outputBuffer, blockCarries, maxWordCount, chunkSize, numBlocks);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AtomicAddWithCarry(uint[] address, int wordIndex, uint valueToAdd, int maxWordCount)
    {
        var carry = valueToAdd;
        var index = wordIndex;
        while (carry > 0 && index < maxWordCount)
        {
            var oldValue = Gpu.AtomicAdd(ref address[index], carry);
            if (oldValue + carry < oldValue)
            {
                carry = 1;
                index++;
            }
            else
            {
                break;
            }
        }
    }


    [GpuDeviceFunction]
    public static void ExecuteForwardNtt(NumberTheoreticTransform args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.Grid.M;
        var length1 = args.Grid.N1;
        var length2 = args.Grid.N2;

        if (transformLength <= 512)
        {
            args.TemporaryBuffer = buffer;
            args.CurrentL = transformLength;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            ExecuteNttSharedRow(args);
            Gpu.SyncGrid();
            return;
        }

        var argsRow1 = args;
        argsRow1.Buffer = temporaryBuffer;
        argsRow1.TemporaryBuffer = temporaryBuffer;
        argsRow1.CurrentL = length1;
        argsRow1.ApplyTwiddleBefore = false;
        argsRow1.ApplyTwiddleAfter = true;
        argsRow1.MaxBlocks = length2;

        var argsRow2 = args;
        argsRow2.TemporaryBuffer = buffer;
        argsRow2.CurrentL = length2;
        argsRow2.ApplyTwiddleBefore = false;
        argsRow2.ApplyTwiddleAfter = false;
        argsRow2.MaxBlocks = length1;

        Gpu.SyncGrid(() =>
        {
            GpuBigIntFermatKernels.DeviceTranspose32X32(buffer, temporaryBuffer, length2, length1, true);
            ExecuteNttSharedRow(argsRow1);
            GpuBigIntFermatKernels.DeviceTranspose32X32(temporaryBuffer, buffer, length1, length2, true);
            ExecuteNttSharedRow(argsRow2);
        });
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void ExecuteInverseNtt(NumberTheoreticTransform args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.Grid.M;
        var length1 = args.Grid.N1;
        var length2 = args.Grid.N2;

        if (transformLength <= 512)
        {
            args.TemporaryBuffer = buffer;
            args.CurrentL = transformLength;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            ExecuteNttSharedRow(args);
            Gpu.SyncGrid();
            return;
        }

        var argsRow1 = args;
        argsRow1.TemporaryBuffer = buffer;
        argsRow1.CurrentL = length2;
        argsRow1.ApplyTwiddleBefore = false;
        argsRow1.ApplyTwiddleAfter = false;
        argsRow1.MaxBlocks = length1;

        var argsRow2 = args;
        argsRow2.Buffer = temporaryBuffer;
        argsRow2.TemporaryBuffer = temporaryBuffer;
        argsRow2.CurrentL = length1;
        argsRow2.ApplyTwiddleBefore = true;
        argsRow2.ApplyTwiddleAfter = false;
        argsRow2.MaxBlocks = length2;

        Gpu.SyncGrid(() =>
        {
            ExecuteNttSharedRow(argsRow1);
            GpuBigIntFermatKernels.DeviceTranspose32X32(buffer, temporaryBuffer, length2, length1, true);
            ExecuteNttSharedRow(argsRow2);
            GpuBigIntFermatKernels.DeviceTranspose32X32(temporaryBuffer, buffer, length1, length2, true);
        });
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void ExecuteDualForwardNtt(DualNtt ctx)
    {
        var args1 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer1,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle1,
            Grid = ctx.Grid,
            P = Prime1,
            Pm = BarrettMagic1
        };
        ExecuteForwardNtt(args1);

        var args2 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer2,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle2,
            Grid = ctx.Grid,
            P = Prime2,
            Pm = BarrettMagic2
        };
        ExecuteForwardNtt(args2);
    }

    [GpuDeviceFunction]
    public static void ExecuteDualInverseNtt(DualNtt ctx)
    {
        var args1 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer1,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle1,
            Grid = ctx.Grid,
            P = Prime1,
            Pm = BarrettMagic1,
            IsInverse = true
        };
        ExecuteInverseNtt(args1);

        var args2 = new NumberTheoreticTransform
        {
            Buffer = ctx.Buffer2,
            TemporaryBuffer = ctx.TemporaryBuffer,
            Twiddle = ctx.Twiddle2,
            Grid = ctx.Grid,
            P = Prime2,
            Pm = BarrettMagic2,
            IsInverse = true
        };
        ExecuteInverseNtt(args2);
    }

    // ReSharper disable once UnusedMember.Global -- Exists solely to trigger snapshot transpilation
    [GpuKernel]
    public static void _DiagnosticsWrapper_ExecuteNttSharedRow(uint[] data)
    {
        // This is a dummy kernel wrapper that exists solely to force the compilation of
        // the [GpuDeviceFunction] methods in this file for snapshot testing.
        NumberTheoreticTransform args = default;
        args.Buffer = data;
        ExecuteForwardNtt(args);
    }
}

