using System;
using System.Numerics;
using Kassyi.Math.GpuBigInt;

namespace Henicos;

public static class ModularReductionGpu
{
    public static BigInteger FermatPRPTest(int E, BigInteger N)
    {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 2; // Extra buffer space

        // 1. Initialize GPU context
        if (!CudaInterop.InitializeGPU(E))
        {
            throw new Exception("GPU Initialization failed.");
        }

        BigInteger result;

        try
        {
            // 2. Allocate buffers
            using var gpuT = new GpuBigInteger(outNumWords);
            using var gpuQ = new GpuBigInteger(outNumWords);
            using var gpuR = new GpuBigInteger(outNumWords);
            using var gpuTemp = new GpuBigInteger(outNumWords);
            using var gpuMaskR = new GpuBigInteger(outNumWords);

            // Initialize Data (X = 2)
            uint[] initHost = new uint[outNumWords];
            initHost[0] = 2;
            using var initData = new GpuBigInteger(initHost);
            gpuT.CopyFrom(initData);

            // Initialize Mask for 2E+1 bits
            uint[] maskHost = new uint[outNumWords];
            int bit2E1 = 2 * E + 1;
            int word2E1 = bit2E1 / 32;
            int rem2E1 = bit2E1 % 32;
            for (int i = 0; i < word2E1; i++) maskHost[i] = 0xFFFFFFFF;
            if (rem2E1 > 0) maskHost[word2E1] = (1U << rem2E1) - 1;
            using var maskData = new GpuBigInteger(maskHost);
            gpuMaskR.CopyFrom(maskData);
            
            Action SquareAndReduce = () =>
            {
                // 1. T = T^2
                gpuT.Square();

                // 2. Q = T >> (2E+1)
                GpuBigInteger.ShiftRight(gpuT, bit2E1, gpuQ);

                // 3. R = T & ((1 << (2E+1)) - 1)
                GpuBigInteger.BitwiseAnd(gpuT, gpuMaskR, gpuR);

                // 4. T' = R - (Q << E) - Q - (Q << 1) - (Q << 3)
                gpuT.CopyFrom(gpuR);

                // T = T - (Q << E)
                GpuBigInteger.ShiftLeft(gpuQ, E, gpuTemp);
                GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);

                // T = T - Q
                GpuBigInteger.Sub(gpuT, gpuQ, gpuT);

                // T = T - (Q << 1)
                GpuBigInteger.ShiftLeft(gpuQ, 1, gpuTemp);
                GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);

                // T = T - (Q << 3)
                GpuBigInteger.ShiftLeft(gpuQ, 3, gpuTemp);
                GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);
            };

            Action MultiplyBy2AndReduce = () =>
            {
                // Multiply by 2 is ShiftLeft by 1
                GpuBigInteger.ShiftLeft(gpuT, 1, gpuT);
            };

            // Loop E-4 times
            for (int i = 0; i < E - 4; i++)
            {
                SquareAndReduce();
            }
            SquareAndReduce(); MultiplyBy2AndReduce();
            SquareAndReduce();
            SquareAndReduce(); MultiplyBy2AndReduce();
            SquareAndReduce();

            // Fetch result back to CPU
            uint[] hostResult = gpuT.ToHostArray();
            
            // Check sign (MSB of the highest word)
            bool isNegative = (hostResult[outNumWords - 1] & 0x80000000) != 0;
            
            byte[] resBytes = new byte[outNumWords * 4 + 1];
            Buffer.BlockCopy(hostResult, 0, resBytes, 0, outNumWords * 4);
            resBytes[outNumWords * 4] = isNegative ? (byte)0xFF : (byte)0x00;

            BigInteger finalVal = new BigInteger(resBytes);
            
            // Final normalization on CPU
            result = finalVal % N;
            if (result < 0) result += N;
        }
        finally
        {
            CudaInterop.CleanupGPU();
        }

        return result;
    }
}
