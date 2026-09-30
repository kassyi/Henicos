using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

public class BlockAddSubParallelTests
{
    private static void RunTest(int numWords, int sign)
    {
        CudaCapability.EnsureGpuAvailable();

        // 1. Generate random data
        var rnd = new Random(12345);
        var aData = new uint[numWords];
        var bData = new uint[numWords];

        for (int i = 0; i < numWords; i++)
        {
            aData[i] = (uint)rnd.Next();
            bData[i] = (uint)rnd.Next();
            
            // To test carry propagation efficiently, occasionally force MaxValue or 0
            if (rnd.Next(10) == 0) aData[i] = 0xFFFFFFFFu;
            if (rnd.Next(10) == 0) bData[i] = 0xFFFFFFFFu;
        }

        if (sign == -1)
        {
            aData[numWords - 1] |= 0x80000000u; // Ensure A is definitely larger
            bData[numWords - 1] &= 0x7FFFFFFFu;
        }

        // 2. Compute expected result using BigInteger
        var aBytes = new byte[numWords * 4];
        var bBytes = new byte[numWords * 4];
        Buffer.BlockCopy(aData, 0, aBytes, 0, aBytes.Length);
        Buffer.BlockCopy(bData, 0, bBytes, 0, bBytes.Length);

        var aBig = new BigInteger(aBytes, isUnsigned: true);
        var bBig = new BigInteger(bBytes, isUnsigned: true);

        BigInteger expectedBig;
        if (sign == 1)
        {
            expectedBig = aBig + bBig;
        }
        else
        {
            expectedBig = aBig - bBig;
        }

        var expectedBytes = expectedBig.ToByteArray(isUnsigned: true, isBigEndian: false);
        var expectedWords = new uint[numWords];
        Buffer.BlockCopy(expectedBytes, 0, expectedWords, 0, System.Math.Min(expectedBytes.Length, expectedWords.Length * 4));

        var resultData = new uint[numWords];

        // blockDim = 256 for this test
        uint blockDimX = 256;

        GpuBigIntFermatKernels.LaunchKernelTestBlockAddSubParallel(
            aData,
            bData,
            resultData,
            numWords,
            sign,
            gridDimX: 1,
            blockDimX: blockDimX
        );

        for (int i = 0; i < numWords; i++)
        {
            Assert.True(expectedWords[i] == resultData[i], $"Mismatch at word {i}. Expected: {expectedWords[i]:X8}, Actual: {resultData[i]:X8}");
        }
    }

    [SkippableFact]
    public void Test_BlockAddSubParallel_Add_Small()
    {
        RunTest(128, 1);
    }

    [SkippableFact]
    public void Test_BlockAddSubParallel_Add_ExactBlock()
    {
        RunTest(256, 1);
    }

    [SkippableFact]
    public void Test_BlockAddSubParallel_Add_Large()
    {
        RunTest(5000, 1);
    }

    [SkippableFact]
    public void Test_BlockAddSubParallel_Sub_Large()
    {
        RunTest(5000, -1);
    }
}
