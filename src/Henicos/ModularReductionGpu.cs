using System;
using System.Numerics;
using Kassyi.Math.GpuBigInt;

namespace Henicos;

public static class ModularReductionGpu
{
    public static BigInteger FermatPRPTest(int E, BigInteger N)
    {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; // Extra buffer space

        // 1. Initialize GPU context
        if (!CudaInterop.InitializeGPU(E))
        {
            throw new Exception("GPU Initialization failed.");
        }


        try
        {
            uint[] resultData = new uint[outNumWords];
            string ilScript = FermatILGenerator.Generate(E);
            
            // Call the C++ native orchestrator directly
            bool success = CudaInterop.GpuBigInt_ExecuteIL(E, ilScript, resultData);
            if (!success)
            {
                throw new Exception("Native GPU FermatPRPTest failed.");
            }

            // Convert to BigInteger for the final exact modulo N
            byte[] resBytes = new byte[resultData.Length * 4 + 1];
            Buffer.BlockCopy(resultData, 0, resBytes, 0, resultData.Length * 4);
            resBytes[resultData.Length * 4] = 0; // force positive
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
