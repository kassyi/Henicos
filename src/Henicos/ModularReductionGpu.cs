using System;
using System.Numerics;
using Kassyi.Math.GpuBigInt;

namespace Henicos;

public static class ModularReductionGpu
{
    public static BigInteger FermatPRPTest(int E, BigInteger N)
    {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 4; // Extra buffer space

        // 1. Initialize GPU context
        if (!CudaInterop.InitializeGPU(E))
        {
            throw new Exception("GPU Initialization failed.");
        }

        BigInteger result;

        try
        {
            // 2. Setup Device Memory
            uint[] initialT = new uint[outNumWords];
            initialT[0] = 2; // T initialized to a=2
            using var gpuT = new GpuBigInteger(initialT); 
            
            // Convert N to uint array and ensure size matches if needed
            var nArray = N.ToByteArray(isUnsigned: true, isBigEndian: false);
            uint[] nUint = new uint[outNumWords];
            Buffer.BlockCopy(nArray, 0, nUint, 0, nArray.Length);
            using var gpuN = new GpuBigInteger(nUint);
            using var gpuQ = new GpuBigInteger(outNumWords);
            using var gpuR = new GpuBigInteger(outNumWords);
            using var gpuTemp = new GpuBigInteger(outNumWords);
            using var gpuMaskR = new GpuBigInteger(outNumWords);

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
                gpuT.Square(numWords + 1);

                // Modular reduction T' = T mod N
                // We perform up to 3 passes of reduction
                for (int pass = 0; pass < 3; pass++)
                {
                    // 2. Q = T >> (2E+1)
                    GpuBigInteger.ShiftRight(gpuT, bit2E1, gpuQ);

                    // 3. R = T & ((1 << (2E+1)) - 1)
                    GpuBigInteger.BitwiseAnd(gpuT, gpuMaskR, gpuR);

                    // 4. T' = R + (Q << E) - Q - (Q << 1) - (Q << 3)
                    gpuT.CopyFrom(gpuR);

                    // T = T + (Q << E)
                    GpuBigInteger.ShiftLeft(gpuQ, E, gpuTemp);
                    GpuBigInteger.Add(gpuT, gpuTemp, gpuT);

                    // T = T - Q
                    GpuBigInteger.Sub(gpuT, gpuQ, gpuT);

                    // T = T - (Q << 1)
                    GpuBigInteger.ShiftLeft(gpuQ, 1, gpuTemp);
                    GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);

                    // T = T - (Q << 3)
                    GpuBigInteger.ShiftLeft(gpuQ, 3, gpuTemp);
                    GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);
                }
            };

            Action MultiplyBy2AndReduce = () =>
            {
                // Multiply by 2
                GpuBigInteger.ShiftLeft(gpuT, 1, gpuT);
                
                // One pass of reduction is enough
                GpuBigInteger.ShiftRight(gpuT, bit2E1, gpuQ);
                GpuBigInteger.BitwiseAnd(gpuT, gpuMaskR, gpuR);
                gpuT.CopyFrom(gpuR);

                GpuBigInteger.ShiftLeft(gpuQ, E, gpuTemp);
                GpuBigInteger.Add(gpuT, gpuTemp, gpuT);

                GpuBigInteger.Sub(gpuT, gpuQ, gpuT);

                GpuBigInteger.ShiftLeft(gpuQ, 1, gpuTemp);
                GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);

                GpuBigInteger.ShiftLeft(gpuQ, 3, gpuTemp);
                GpuBigInteger.Sub(gpuT, gpuTemp, gpuT);
            };

            // Loop E times for the MSB '1's
            for (int i = 0; i < E; i++)
            {
                SquareAndReduce();
                MultiplyBy2AndReduce();
            }

            // Loop E-4 times for the middle '0's
            for (int i = 0; i < E - 4; i++)
            {
                SquareAndReduce();
            }

            // The remaining exponent bits are 1010
            SquareAndReduce();
            MultiplyBy2AndReduce(); // bit 3 (1)

            SquareAndReduce(); // bit 2 (0)

            SquareAndReduce();
            MultiplyBy2AndReduce(); // bit 1 (1)

            SquareAndReduce(); // bit 0 (0)

            var tmp = gpuT.ToHostArray();
            byte[] resBytes = new byte[tmp.Length * 4 + 1];
            Buffer.BlockCopy(tmp, 0, resBytes, 0, tmp.Length * 4);
            resBytes[tmp.Length * 4] = 0; // force positive
            BigInteger res = new BigInteger(resBytes);
            res = res % N; // Exact normalization
            return res;
        }
        finally
        {
            CudaInterop.CleanupGPU();
        }
    }
}
