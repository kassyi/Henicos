// ReSharper disable All
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
public static class GpuBigIntNttKernels
{
    // NTT Primes and primitive roots
    // We use two 32-bit primes for CRT to support convolution values up to 2^55
    // P1 = 63 * 2^26 + 1 (Proth prime)
    public const uint Prime1 = 4253024257; // 素数 (507 * 2^23 + 1, or something similar)
    public const uint NttG1 = 5; // 原始根

    // NTT素数 2
    public const uint Prime2 = 4293918721; // 素数 (4095 * 2^20 + 1)
    public const uint NttG2 = 19; // 原始根

    // Barrett Reduction Constants
    // M = floor(2^64 / P)
    // M1 = floor(2^64 / 4253024257) = 4337323974
    public const ulong BarrettMagic1 = 4337323974UL; // for P1

    // M2 = floor(2^64 / 4293918721) = 4296016127
    public const ulong BarrettMagic2 = 4296016127UL; // for P2

    [GpuDeviceFunction]
    public static uint ModAdd(uint operandA, uint operandB, uint primeModulus)
    {
        if (operandA >= primeModulus - operandB)
        {
            return operandA - (primeModulus - operandB);
        }

        return operandA + operandB;
    }

    [GpuDeviceFunction]
    public static uint ModSub(uint operandA, uint operandB, uint primeModulus)
    {
        var result = operandA - operandB;
        if (operandA < operandB)
        {
            result += primeModulus;
        }

        return result;
    }

    [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse",
        Justification = "Variables are modified via inline PTX assembly which ReSharper cannot detect")]
    [GpuDeviceFunction]
    [SuppressMessage("ReSharper", "UselessBinaryOperation",
        Justification = "Variables are modified via inline PTX assembly which ReSharper cannot detect")]
    public static uint ModMulBarrett(uint operandA, uint operandB, uint primeModulus, ulong barrettMagic)
    {
        ulong product = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.wide.u32 %0, %1, %2;\" : \"=l\"({product}) : \"r\"({operandA}), \"r\"({operandB}));");

        ulong quotient = 0;
        Gpu.InlinePtx(
            $"asm volatile(\"mul.hi.u64 %0, %1, %2;\" : \"=l\"({quotient}) : \"l\"({product}), \"l\"({barrettMagic}));");

        var remainder = product - quotient * primeModulus;
        while (remainder >= primeModulus)
        {
            remainder -= primeModulus;
        }

        return (uint)remainder;
    }
    // ReSharper restore ConditionIsAlwaysTrueOrFalse
    // ReSharper restore UselessBinaryOperation

    [GpuDeviceFunction]
    public static uint ModMul1(uint operandA, uint operandB) =>
        ModMulBarrett(operandA, operandB, Prime1, BarrettMagic1);

    [GpuDeviceFunction]
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
    public static void Device_NTT_Shared_Row(ref NumberTheoreticTransformKernelArgs args)
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
                Device_NTT_LoadAndTwiddleBefore(rowIndex, threadId, currentLength, maxLog2Length, transformLength,
                    applyTwiddleBefore, primeModulus, barrettMagic, deviceData, deviceTwiddle);
            }

            Gpu.SyncThreads();

            for (int step = 0; step < maxLog2Length; step++)
            {
                for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
                {
                    Device_NTT_ButterflyStep(step, threadId, transformLength, primeModulus, barrettMagic,
                        deviceTwiddle);
                }

                Gpu.SyncThreads();
            }

            for (int threadId = ThreadIndex.X; threadId < threadsNeeded; threadId += BlockDimension.X)
            {
                Device_NTT_TwiddleAfterAndStore(rowIndex, threadId, currentLength, transformLength, applyTwiddleAfter,
                    primeModulus, barrettMagic, deviceData, deviceTwiddle);
            }

            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Device_NTT_LoadAndTwiddleBefore(
        int rowIndex, int threadId, int currentLength, int maxLog2Length, int transformLength,
        bool applyTwiddleBefore, uint primeModulus, ulong barrettMagic,
        [GpuConst] uint[] deviceData, [GpuConst] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var blockOffset = rowIndex * currentLength;
        var value1 = deviceData[blockOffset + threadId];
        var value2 = deviceData[blockOffset + threadId + currentLength / 2];

        uint reversedThreadId1 = 0;
        uint reversedThreadId2 = 0;
        Gpu.InlinePtx($"{reversedThreadId1} = __brev({threadId}) >> (32 - {maxLog2Length});");
        Gpu.InlinePtx($"{reversedThreadId2} = __brev({threadId + currentLength / 2}) >> (32 - {maxLog2Length});");

        if (applyTwiddleBefore)
        {
            var twiddleIndex1 = (ulong)rowIndex * (ulong)threadId;
            twiddleIndex1 %= (ulong)transformLength;
            value1 = ModMulBarrett(value1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);

            var twiddleIndex2 = (ulong)rowIndex * (ulong)(threadId + currentLength / 2);
            twiddleIndex2 %= (ulong)transformLength;
            value2 = ModMulBarrett(value2, deviceTwiddle[twiddleIndex2], primeModulus, barrettMagic);
        }

        Gpu.InlinePtx($"s_row[{reversedThreadId1}] = {value1};");
        Gpu.InlinePtx($"s_row[{reversedThreadId2}] = {value2};");
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Device_NTT_ButterflyStep(
        int step, int threadId, int transformLength,
        uint primeModulus, ulong barrettMagic,
        [GpuConst] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var length = 2 << step;
        var halfLength = length / 2;
        var blockIndex = threadId / halfLength;
        var elementIndex = threadId % halfLength;
        var position = blockIndex * length + elementIndex;

        var twiddleFactorIndex = elementIndex * (transformLength / length);
        var twiddleFactor = deviceTwiddle[twiddleFactorIndex];

        uint leftTerm = 0;
        Gpu.InlinePtx($"{leftTerm} = s_row[{position}];");

        uint rightTermSource = 0;
        Gpu.InlinePtx($"{rightTermSource} = s_row[{position + halfLength}];");
        var rightTerm = ModMulBarrett(rightTermSource, twiddleFactor, primeModulus, barrettMagic);

        var additionResult = ModAdd(leftTerm, rightTerm, primeModulus);
        var subtractionResult = ModSub(leftTerm, rightTerm, primeModulus);

        Gpu.InlinePtx($"s_row[{position}] = {additionResult};");
        Gpu.InlinePtx($"s_row[{position + halfLength}] = {subtractionResult};");
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Device_NTT_TwiddleAfterAndStore(
        int rowIndex, int threadId, int currentLength, int transformLength,
        bool applyTwiddleAfter, uint primeModulus, ulong barrettMagic,
        uint[] deviceData, [GpuConst] uint[] deviceTwiddle)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_row[];");
        var blockOffset = rowIndex * currentLength;

        if (applyTwiddleAfter)
        {
            var twiddleIndex1 = (ulong)rowIndex * (ulong)threadId;
            twiddleIndex1 %= (ulong)transformLength;
            uint result1 = 0;
            Gpu.InlinePtx($"{result1} = s_row[{threadId}];");
            result1 = ModMulBarrett(result1, deviceTwiddle[twiddleIndex1], primeModulus, barrettMagic);
            Gpu.InlinePtx($"s_row[{threadId}] = {result1};");

            var twiddleIndex2 = (ulong)rowIndex * (ulong)(threadId + currentLength / 2);
            twiddleIndex2 %= (ulong)transformLength;
            uint result2 = 0;
            Gpu.InlinePtx($"{result2} = s_row[{threadId + currentLength / 2}];");
            result2 = ModMulBarrett(result2, deviceTwiddle[twiddleIndex2], primeModulus, barrettMagic);
            Gpu.InlinePtx($"s_row[{threadId + currentLength / 2}] = {result2};");
        }

        uint output1 = 0;
        uint output2 = 0;
        Gpu.InlinePtx($"{output1} = s_row[{threadId}];");
        Gpu.InlinePtx($"{output2} = s_row[{threadId + currentLength / 2}];");

        deviceData[blockOffset + threadId] = output1;
        deviceData[blockOffset + threadId + currentLength / 2] = output2;
    }

    [GpuDeviceFunction]
    public static void Device_NTT_PointwiseSquare(uint[] deviceData1, uint[] deviceData2, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1[index] = ModMulBarrett(deviceData1[index], deviceData1[index], Prime1, BarrettMagic1);
            deviceData2[index] = ModMulBarrett(deviceData2[index], deviceData2[index], Prime2, BarrettMagic2);
        });
    }

    [GpuDeviceFunction]
    public static void Device_NTT_PointwiseMultiply(uint[] deviceData1A, uint[] deviceData2A, uint[] deviceData1B,
        uint[] deviceData2B, int transformLength)
    {
        Gpu.GridStrideLoop(transformLength, index =>
        {
            deviceData1A[index] = ModMulBarrett(deviceData1A[index], deviceData1B[index], Prime1, BarrettMagic1);
            deviceData2A[index] = ModMulBarrett(deviceData2A[index], deviceData2B[index], Prime2, BarrettMagic2);
        });
    }

    [GpuDeviceFunction]
    public static void Device_CRT_ComputeCoefficients([GpuConst] uint[] deviceResult1, [GpuConst] uint[] deviceResult2,
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
            if (value2 >= value1)
            {
                difference = value2 - value1;
            }
            else
            {
                difference = value2 + Modulus2 - value1;
            }

            const ulong Prime1InverseModPrime2 = 105UL;
            var crtCoefficient = (difference * Prime1InverseModPrime2) % Modulus2;
            var recombinedValue = value1 + Modulus1 * crtCoefficient;

            uncarriedLow[index] = (uint)(recombinedValue & 0xFFFFFFFF);
            uncarriedHigh[index] = (uint)(recombinedValue >> 32);
        });
    }

    [GpuDeviceFunction]
    public static void Device_ResolveNttCarries(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, int transformLength, int maxWordCount)
    {
        Gpu.SingleThread(() =>
        {
            ulong carry = 0;

            for (int i = 0; i < maxWordCount; i++)
            {
                ulong sum0 = carry;
                if (2 * i < transformLength)
                {
                    sum0 += uncarriedLow[2 * i] | ((ulong)uncarriedHigh[2 * i] << 32);
                }

                ulong sum1 = 0;
                if (2 * i + 1 < transformLength)
                {
                    sum1 = uncarriedLow[2 * i + 1] | ((ulong)uncarriedHigh[2 * i + 1] << 32);
                }

                var low16 = (uint)(sum0 & 0xFFFF);
                var carry0 = sum0 >> 16;
                var sum1_with_carry = sum1 + carry0;
                var high16 = (uint)(sum1_with_carry & 0xFFFF);

                carry = sum1_with_carry >> 16;

                outputBuffer[i] = low16 | (high16 << 16);
            }
        });
    }

    [GpuKernel]
    public static void Kernel_ResolveNttCarries(uint[] uncarriedLow, uint[] uncarriedHigh,
        uint[] outputBuffer, int transformLength, int maxWordCount)
    {
        Device_ResolveNttCarries(uncarriedLow, uncarriedHigh, outputBuffer, transformLength, maxWordCount);
    }


    [GpuDeviceFunction]
    public static void Device_NTT_Internal(ref NumberTheoreticTransformKernelArgs args)
    {
        var buffer = args.Buffer;
        var temporaryBuffer = args.TemporaryBuffer;
        var transformLength = args.M;
        var length1 = args.N1;
        var length2 = args.N2;
        var isInverse = args.IsInverse;
        if (transformLength <= 512)
        {
            args.CurrentL = transformLength;
            args.ApplyTwiddleBefore = false;
            args.ApplyTwiddleAfter = false;
            args.MaxBlocks = 1;
            Device_NTT_Shared_Row(ref args);
            Gpu.SyncGrid();
        }
        else
        {
            if (isInverse)
            {
                args.CurrentL = length2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = length1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(buffer, temporaryBuffer, length2, length1);
                Gpu.SyncGrid();

                args.Buffer = temporaryBuffer;
                args.CurrentL = length1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = length2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buffer; // Restore buffer
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(temporaryBuffer, buffer, length1, length2);
                Gpu.SyncGrid();
            }
            else
            {
                GpuBigIntFermatKernels.Device_Transpose_32x32(buffer, temporaryBuffer, length2, length1);
                Gpu.SyncGrid();

                args.Buffer = temporaryBuffer;
                args.CurrentL = length1;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = true;
                args.MaxBlocks = length2;
                Device_NTT_Shared_Row(ref args);
                args.Buffer = buffer; // Restore buffer
                Gpu.SyncGrid();

                GpuBigIntFermatKernels.Device_Transpose_32x32(temporaryBuffer, buffer, length1, length2);
                Gpu.SyncGrid();

                args.CurrentL = length2;
                args.ApplyTwiddleBefore = false;
                args.ApplyTwiddleAfter = false;
                args.MaxBlocks = length1;
                Device_NTT_Shared_Row(ref args);
                Gpu.SyncGrid();
            }
        }
    }

    [GpuDeviceFunction]
    public static void Device_ForwardNTT(ref NumberTheoreticTransformKernelArgs args)
    {
        args.IsInverse = false;
        Device_NTT_Internal(ref args);
    }

    [GpuDeviceFunction]
    public static void Device_InverseNTT(ref NumberTheoreticTransformKernelArgs args)
    {
        args.IsInverse = true;
        Device_NTT_Internal(ref args);
    }

    [GpuKernel]
    [SuppressMessage("ReSharper", "UnusedMember.Global",
        Justification = "Exists solely to trigger snapshot transpilation")]
    public static void DummyTranspilationKernel(uint[] data)
    {
        // This kernel exists solely to ensure that the G# source generator transpiles 
        // the [GpuDeviceFunction] methods in this file for snapshot testing.
        NumberTheoreticTransformKernelArgs args = default;
        args.Buffer = data;
        Device_ForwardNTT(ref args);
    }
}

