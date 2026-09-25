// ReSharper disable All

#pragma warning disable GSH0014

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntNttKernels
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
    public static void ExecuteNttSharedRow(NumberTheoreticTransformKernelArgs args)
    {
        var deviceData = args.Buffer;
        var deviceTwiddle = args.Twiddle;
        var transformLength = args.M;
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
                    primeModulus, barrettMagic, deviceData, deviceTwiddle);
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
            var twiddleIndex1 = (ulong)rowIndex * (ulong)threadId;
            twiddleIndex1 %= (ulong)transformLength;
            value1 = ModMulBarrett(value1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);

            var twiddleIndex2 = (ulong)rowIndex * (ulong)(threadId + currentLength / 2);
            twiddleIndex2 %= (ulong)transformLength;
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
        var length = 2 << step;
        var halfLength = length / 2;
        var blockIndex = threadId / halfLength;
        var elementIndex = threadId % halfLength;
        var position = blockIndex * length + elementIndex;

        var twiddleFactorIndex = elementIndex * (transformLength / length);
        var twiddleFactor = deviceTwiddle[twiddleFactorIndex];

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
        uint[] deviceData, [GpuReadOnly] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var blockOffset = rowIndex * currentLength;

        uint padId1 = (uint)(threadId + (threadId >> 5));
        uint padId2 = (uint)((threadId + currentLength / 2) + ((threadId + currentLength / 2) >> 5));

        if (applyTwiddleAfter)
        {
            var twiddleIndex1 = (ulong)rowIndex * (ulong)threadId;
            twiddleIndex1 %= (ulong)transformLength;
            uint result1 = 0;
            Gpu.InlinePtx($"{result1} = s_row[{padId1}];");
            result1 = ModMulBarrett(result1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);
            Gpu.InlinePtx($"s_row[{padId1}] = {result1};");

            var twiddleIndex2 = (ulong)rowIndex * (ulong)(threadId + currentLength / 2);
            twiddleIndex2 %= (ulong)transformLength;
            uint result2 = 0;
            Gpu.InlinePtx($"{result2} = s_row[{padId2}];");
            result2 = ModMulBarrett(result2, deviceTwiddle[twiddleIndex2], primeModulus, barrettMagic);
            Gpu.InlinePtx($"s_row[{padId2}] = {result2};");
        }

        uint output1 = 0;
        uint output2 = 0;
        Gpu.InlinePtx($"{output1} = s_row[{padId1}];");
        Gpu.InlinePtx($"{output2} = s_row[{padId2}];");

        deviceData[blockOffset + threadId] = output1;
        deviceData[blockOffset + threadId + currentLength / 2] = output2;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeNttLocalSum(uint[] uncarriedLow, uint[] uncarriedHigh, int i, int maxWordCount, int transformLength)
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
                        localSum += (uint)(c << 16);
                    }
                    else
                    {
                        localSum += (uint)(c >> ((step - 1) * 16));
                    }
                }
            });
        }
        return localSum;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeBlockCarryScan(
        uint g, uint p,
        SharedMemory<uint> sharedCarry,
        int laneId, int warpId, int bDim,
        out uint scanG, out uint scanP, out uint carryIn)
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
            var valState = laneId < numWarps ? GpuBigIntKernels.UnpackCarryState(sharedCarry[laneId]) : new GpuBigIntKernels.CarryState(0, 0);
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

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesBlocked(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        var bIdx = BlockIndex.X;
        var tIdx = ThreadIndex.X;

        var numBlocks = (maxWordCount + chunkSize - 1) / chunkSize;
        if (bIdx < numBlocks)
        {
            int startIdx = bIdx * chunkSize;
            int i = startIdx + tIdx;

            ulong localSum = ComputeNttLocalSum(uncarriedLow, uncarriedHigh, i, maxWordCount, transformLength);

            uint L = (uint)(localSum & 0xFFFFFFFFul);
            uint H = (uint)(localSum >> 32);

            SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
            SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

            int laneId = tIdx % 32;
            int warpId = tIdx / 32;
            int bDim = BlockDimension.X;

            uint prevH = ResolveOverlapH(H, sharedH, laneId, warpId);

            ulong sum = (ulong)L + prevH;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            ComputeBlockCarryScan(g, p, sharedCarry, laneId, warpId, bDim, out _, out _, out uint carryIn);

            if (i < maxWordCount)
            {
                outputBuffer[i] = (uint)sum + carryIn;
            }

            if (tIdx == 0)
            {
                uint lastH = sharedH[((bDim + 31) / 32) - 1];
                uint waveCarry = GpuBigIntKernels.ApplyCarry(GpuBigIntKernels.UnpackCarryState(sharedCarry[((bDim + 31) / 32) - 1]), 0u);
                blockCarries[bIdx] = lastH + waveCarry;
            }

            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void ResolveNttCarriesBlockedKernel(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize)
    {
        ExecuteResolveNttCarriesBlocked(uncarriedLow, uncarriedHigh, outputBuffer, blockCarries, transformLength, maxWordCount, chunkSize);
    }

    [GpuDeviceFunction]
    public static void ExecuteResolveNttCarriesApply(uint[] outputBuffer, uint[] blockCarries, int maxWordCount,
        int chunkSize, int numBlocks)
    {
        var globalBlockIdx = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (globalBlockIdx >= numBlocks)
        {
            return;
        }

        var carry = blockCarries[globalBlockIdx];
        if (carry > 0)
        {
            int startIdx = (globalBlockIdx + 1) * chunkSize;
            AtomicAddWithCarry(outputBuffer, startIdx, carry, maxWordCount);
        }
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
    public static void ExecuteForwardNtt(NumberTheoreticTransformKernelArgs args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.M;
        var length1 = args.N1;
        var length2 = args.N2;

        if (transformLength <= 512)
        {
            args.CurrentL = transformLength;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            ExecuteNttSharedRow(args);
            Gpu.SyncGrid();
            return;
        }

        GpuBigIntFermatKernels.DeviceTranspose32X32(buffer, temporaryBuffer, length2, length1, true);
        Gpu.SyncGrid();

        args.Buffer = temporaryBuffer;
        args.CurrentL = length1;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = true;
        args.MaxBlocks = length2;
        ExecuteNttSharedRow(args);
        args.Buffer = buffer; // Restore buffer
        Gpu.SyncGrid();

        GpuBigIntFermatKernels.DeviceTranspose32X32(temporaryBuffer, buffer, length1, length2, true);
        Gpu.SyncGrid();

        args.CurrentL = length2;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = false;
        args.MaxBlocks = length1;
        ExecuteNttSharedRow(args);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void ExecuteInverseNtt(NumberTheoreticTransformKernelArgs args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.M;
        var length1 = args.N1;
        var length2 = args.N2;

        if (transformLength <= 512)
        {
            args.CurrentL = transformLength;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            ExecuteNttSharedRow(args);
            Gpu.SyncGrid();
            return;
        }

        args.CurrentL = length2;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = true;
        args.MaxBlocks = length1;
        ExecuteNttSharedRow(args);
        Gpu.SyncGrid();

        GpuBigIntFermatKernels.DeviceTranspose32X32(buffer, temporaryBuffer, length2, length1, true);
        Gpu.SyncGrid();

        args.Buffer = temporaryBuffer;
        args.CurrentL = length1;
        args.ApplyTwiddleBefore = false;
        args.ApplyTwiddleAfter = false;
        args.MaxBlocks = length2;
        ExecuteNttSharedRow(args);
        args.Buffer = buffer; // Restore buffer
        Gpu.SyncGrid();

        GpuBigIntFermatKernels.DeviceTranspose32X32(temporaryBuffer, buffer, length1, length2, true);
        Gpu.SyncGrid();
    }

    // ReSharper disable once UnusedMember.Global -- Exists solely to trigger snapshot transpilation
    [GpuKernel]
    public static void _DiagnosticsWrapper_ExecuteNttSharedRow(uint[] data)
    {
        // This is a dummy kernel wrapper that exists solely to force the compilation of
        // the [GpuDeviceFunction] methods in this file for snapshot testing.
        NumberTheoreticTransformKernelArgs args = default;
        args.Buffer = data;
        ExecuteForwardNtt(args);
    }
}

