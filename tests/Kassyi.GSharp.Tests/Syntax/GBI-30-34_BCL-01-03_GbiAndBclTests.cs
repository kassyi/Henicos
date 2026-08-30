using System;
using System.Numerics;

namespace Kassyi.GSharp.Tests.Syntax;

public class GBI_30_34_BCL_01_03_GbiAndBclTests
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GBI-30")]
    public void Test_GBI30_ThreadIndex3D_LaunchAndValidation()
    {
        uint gridX = 2, gridY = 2, gridZ = 1;
        uint blockX = 4, blockY = 2, blockZ = 2;
        int totalThreads = (int)(gridX * gridY * gridZ * blockX * blockY * blockZ);

        int[] threadIndices = new int[totalThreads * 3];
        int[] blockIndices = new int[totalThreads * 3];
        int[] blockDimensions = new int[totalThreads * 3];
        int[] gridDimensions = new int[totalThreads * 3];

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_ThreadIndex3DKernel(
            threadIndices,
            blockIndices,
            blockDimensions,
            gridDimensions,
            gridDimX: gridX, gridDimY: gridY, gridDimZ: gridZ,
            blockDimX: blockX, blockDimY: blockY, blockDimZ: blockZ
        );

        int threadsPerBlock = (int)(blockX * blockY * blockZ);

        for (int bx = 0; bx < gridX; bx++)
        {
            for (int by = 0; by < gridY; by++)
            {
                for (int bz = 0; bz < gridZ; bz++)
                {
                    int blockIdx1D = bx + by * (int)gridX + bz * ((int)gridX * (int)gridY);

                    for (int tx = 0; tx < blockX; tx++)
                    {
                        for (int ty = 0; ty < blockY; ty++)
                        {
                            for (int tz = 0; tz < blockZ; tz++)
                            {
                                int localIdx = tx + ty * (int)blockX + tz * ((int)blockX * (int)blockY);
                                int globalIdx = blockIdx1D * threadsPerBlock + localIdx;
                                int baseIdx = globalIdx * 3;

                                Assert.Equal(tx, threadIndices[baseIdx + 0]);
                                Assert.Equal(ty, threadIndices[baseIdx + 1]);
                                Assert.Equal(tz, threadIndices[baseIdx + 2]);

                                Assert.Equal(bx, blockIndices[baseIdx + 0]);
                                Assert.Equal(by, blockIndices[baseIdx + 1]);
                                Assert.Equal(bz, blockIndices[baseIdx + 2]);

                                Assert.Equal((int)blockX, blockDimensions[baseIdx + 0]);
                                Assert.Equal((int)blockY, blockDimensions[baseIdx + 1]);
                                Assert.Equal((int)blockZ, blockDimensions[baseIdx + 2]);

                                Assert.Equal((int)gridX, gridDimensions[baseIdx + 0]);
                                Assert.Equal((int)gridY, gridDimensions[baseIdx + 1]);
                                Assert.Equal((int)gridZ, gridDimensions[baseIdx + 2]);
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GBI-31")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    public void Test_GBI31_SharedMemory_SyncThreadsAcrossWarps(int length)
    {
        int[] input = new int[Math.Max(length, 1)]; 
        int[] output = new int[Math.Max(length, 1)];

        if (length > 0)
        {
            input = new int[length];
            output = new int[length];
            for (int i = 0; i < length; i++)
            {
                input[i] = (i + 1) * 10;
            }
        }
        else
        {
            input = [];
            output = [];
        }

        uint threadsToLaunch = (uint)Math.Max(length + 32, 64);

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_SharedMemorySyncKernel(
            input,
            output,
            length,
            blockDimX: threadsToLaunch,
            sharedMemBytes: (uint)(128 * sizeof(int))
        );

        for (int i = 0; i < length; i++)
        {
            int expectedNeighborIdx = (i + 32) % length;
            Assert.Equal(input[expectedNeighborIdx], output[i]);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("Category", "Stress")]
    [Trait("FeatureId", "GBI-32")]
    public void Test_GBI32_AtomicAdd_HighContentionStressTest()
    {
        int[] globalSum = [0];
        uint blocksPerGrid = 16;
        uint threadsPerBlock = 256;
        int expectedTotalThreads = (int)(blocksPerGrid * threadsPerBlock); // 4096 threads

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_AtomicAddStressKernel(
            globalSum,
            gridDimX: blocksPerGrid,
            blockDimX: threadsPerBlock
        );

        Assert.Equal(expectedTotalThreads, globalSum[0]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GBI-33")]
    [InlineData(new uint[] { 0u }, new uint[] { 0u }, new uint[] { 0u }, 0u)]
    [InlineData(new uint[] { 100u }, new uint[] { 200u }, new uint[] { 300u }, 0u)]
    [InlineData(new uint[] { 0xFFFFFFFFu }, new uint[] { 1u }, new uint[] { 0u }, 1u)]
    [InlineData(new uint[] { 0xFFFFFFFFu }, new uint[] { 0xFFFFFFFFu }, new uint[] { 0xFFFFFFFEu }, 1u)]
    [InlineData(new uint[] { 0x80000000u }, new uint[] { 0x80000000u }, new uint[] { 0u }, 1u)]
    [InlineData(new uint[] { 0xFFFFFFFFu, 0xFFFFFFFFu, 0u }, new uint[] { 1u, 0u, 0u }, new uint[] { 0u, 0u, 1u }, 0u)]
    [InlineData(new uint[] { 0xFFFFFFFFu, 0xFFFFFFFFu }, new uint[] { 1u, 0u }, new uint[] { 0u, 0u }, 1u)]
    public void Test_GBI33_MultiDigitCarry_PtxReplacement(uint[] a, uint[] b, uint[] expectedResult, uint expectedCarry)
    {
        uint[] result = new uint[a.Length];
        uint[] carries = [0u];

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_MultiDigitAddKernel(
            a, b, result, carries, a.Length, blockDimX: 1
        );

        for (int i = 0; i < expectedResult.Length; i++)
        {
            Assert.Equal(expectedResult[i], result[i]);
        }
        Assert.Equal(expectedCarry, carries[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GBI-34")]
    public void Test_GBI34_InlinePtx_Execution()
    {
        uint[] data = [0u];

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_InlinePtxExecutionKernel(
            data, blockDimX: 2 // Launch more than 1 thread to test the fallback 'tid != 0' path
        );

        Assert.Equal(7777u, data[0]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "BCL-01")]
    [Trait("FeatureId", "BCL-02")]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    [InlineData(0x80000000u)]
    [InlineData(0x55555555u)]
    [InlineData(0xAAAAAAAAu)]
    [InlineData(0b00000000_10101010_11110000_00001000u)]
    public void Test_BCL01_BCL02_BitOperations_BoundaryDataDriven(uint input)
    {
        uint[] u32Inputs = [input];
        uint[] u32Results = new uint[6];

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_BitOperationsTestKernel(
            u32Inputs, u32Results, blockDimX: 1
        );

        Assert.Equal((uint)BitOperations.PopCount(input), u32Results[0]);
        Assert.Equal((uint)BitOperations.Log2(input), u32Results[1]);
        Assert.Equal((uint)BitOperations.LeadingZeroCount(input), u32Results[2]);
        Assert.Equal((uint)BitOperations.TrailingZeroCount(input), u32Results[3]);
        Assert.Equal(BitOperations.RotateLeft(input, 5), u32Results[4]);
        Assert.Equal(BitOperations.RotateRight(input, 3), u32Results[5]);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "BCL-03")]
    [InlineData(0.0, 0.0f)]
    [InlineData(-0.0, -0.0f)]
    [InlineData(1.5, 1.5f)]
    [InlineData(-1.5, -1.5f)]
    [InlineData(25.0, 25.0f)]
    [InlineData(-25.0, -25.0f)]
    [InlineData(double.NaN, float.NaN)]
    [InlineData(double.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, float.NegativeInfinity)]
    [InlineData(double.MinValue, float.MinValue)]
    [InlineData(double.MaxValue, float.MaxValue)]
    public void Test_BCL03_MathAndMathF_EdgeCases(double dVal, float fVal)
    {
        double[] dInputs = [dVal];
        double[] dResults = new double[8];
        float[] fInputs = [fVal];
        float[] fResults = new float[8];

        GBI_30_34_BCL_01_03_GbiAndBclKernelsWrapper.Launch_MathFunctionsKernel(
            dInputs, dResults, fInputs, fResults, blockDimX: 1
        );

        // Double Assertions
        AssertEqualOrNaN(Math.Abs(dVal), dResults[0]);
        AssertEqualOrNaN(Math.Sqrt(Math.Abs(dVal)), dResults[1]);
        AssertEqualOrNaN(Math.Sin(dVal), dResults[2]);
        AssertEqualOrNaN(Math.Cos(dVal), dResults[3]);
        AssertEqualOrNaN(Math.Max(dVal, 10.0), dResults[4], fallbackIfNaN: 10.0);
        AssertEqualOrNaN(Math.Min(dVal, -10.0), dResults[5], fallbackIfNaN: -10.0);
        AssertEqualOrNaN(Math.Floor(dVal), dResults[6]);
        AssertEqualOrNaN(Math.Ceiling(dVal), dResults[7]);

        // Float Assertions
        AssertEqualOrNaNF(MathF.Abs(fVal), fResults[0]);
        AssertEqualOrNaNF(MathF.Sqrt(MathF.Abs(fVal)), fResults[1]);
        AssertEqualOrNaNF(MathF.Sin(fVal), fResults[2]);
        AssertEqualOrNaNF(MathF.Cos(fVal), fResults[3]);
        AssertEqualOrNaNF(MathF.Max(fVal, 10.0f), fResults[4], fallbackIfNaN: 10.0f);
        AssertEqualOrNaNF(MathF.Min(fVal, -10.0f), fResults[5], fallbackIfNaN: -10.0f);
        AssertEqualOrNaNF(MathF.Floor(fVal), fResults[6]);
        AssertEqualOrNaNF(MathF.Ceiling(fVal), fResults[7]);
    }

    private static void AssertEqualOrNaN(double expected, double actual, double? fallbackIfNaN = null)
    {
        if (double.IsNaN(expected))
        {
            if (fallbackIfNaN.HasValue && actual == fallbackIfNaN.Value) return;
            Assert.True(double.IsNaN(actual));
        }
        else
        {
            Assert.Equal(expected, actual, precision: 12);
        }
    }

    private static void AssertEqualOrNaNF(float expected, float actual, float? fallbackIfNaN = null)
    {
        if (float.IsNaN(expected))
        {
            if (fallbackIfNaN.HasValue && actual == fallbackIfNaN.Value) return;
            Assert.True(float.IsNaN(actual));
        }
        else
        {
            Assert.Equal(expected, actual, precision: 5);
        }
    }
}
