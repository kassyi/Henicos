namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ParallelAdderTests
{
    private static void ComputeExpected(uint[] aData, uint[] bData, int numWords, int sign, uint[] expectedWords)
    {
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
            var modulus = BigInteger.One << (numWords * 32);
            expectedBig = (aBig - bBig + modulus) % modulus;
        }

        var expectedBytes = expectedBig.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Clear(expectedWords, 0, expectedWords.Length);
        Buffer.BlockCopy(expectedBytes, 0, expectedWords, 0, Math.Min(expectedBytes.Length, numWords * 4));
    }

    private static (uint[] A, uint[] B, uint[] Expected) CreateRandomTestVectors(int numWords, int sign, int seed = 54321)
    {
        var rnd = new Random(seed + numWords * 31 + sign);
        var aData = new uint[numWords];
        var bData = new uint[numWords];

        for (int i = 0; i < numWords; i++)
        {
            aData[i] = (uint)rnd.Next();
            bData[i] = (uint)rnd.Next();

            var mode = rnd.Next(12);
            if (mode == 0)
            {
                aData[i] = 0xFFFFFFFFu;
                bData[i] = 0u;
            }
            else if (mode == 1)
            {
                aData[i] = 0u;
                bData[i] = 0xFFFFFFFFu;
            }
        }

        if (sign == -1 && numWords > 0)
        {
            aData[numWords - 1] |= 0x80000000u;
            bData[numWords - 1] &= 0x7FFFFFFFu;
        }

        var expectedWords = new uint[numWords];
        ComputeExpected(aData, bData, numWords, sign, expectedWords);
        return (aData, bData, expectedWords);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(5000)]
    public void WarpParallelAdd_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: 1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchWarpParallelAddKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(5000)]
    public void WarpParallelSub_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: -1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchWarpParallelSubKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    public void WarpBrentKungParallelAdd_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: 1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchWarpBrentKungParallelAddKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    public void WarpBrentKungParallelSub_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: -1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchWarpBrentKungParallelSubKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(5000)]
    public void BrentKungBlockParallelAdd_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: 1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchBrentKungParallelAddKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 256);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(5000)]
    public void BrentKungBlockParallelSub_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (aData, bData, expectedWords) = CreateRandomTestVectors(numWords, sign: -1);
        var resultData = new uint[numWords];

        ParallelAdder.LaunchBrentKungParallelSubKernel(
            aData,
            bData,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 256);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultData));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void FullRippleCarry_AllMaxPlusOne_WrapsToZeroAcrossLanesAndChunks(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var aData = new uint[numWords];
        var bData = new uint[numWords];
        Array.Fill(aData, 0xFFFFFFFFu);
        bData[0] = 1u;

        var resultWarpKs = new uint[numWords];
        var resultWarpBk = new uint[numWords];
        var resultBlockBk = new uint[numWords];
        Array.Fill(resultWarpKs, 0xDEADBEAFu);
        Array.Fill(resultWarpBk, 0xDEADBEAFu);
        Array.Fill(resultBlockBk, 0xDEADBEAFu);

        ParallelAdder.LaunchWarpParallelAddKernel(aData, bData, resultWarpKs, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchWarpBrentKungParallelAddKernel(aData, bData, resultWarpBk, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchBrentKungParallelAddKernel(aData, bData, resultBlockBk, numWords, gridDimX: 1, blockDimX: 256);

        for (int i = 0; i < numWords; i++)
        {
            Assert.Equal(0u, resultWarpKs[i]);
            Assert.Equal(0u, resultWarpBk[i]);
            Assert.Equal(0u, resultBlockBk[i]);
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void FullRippleBorrow_PowerOfTwoMinusOne_PropagatesAcrossAllWords(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var aData = new uint[numWords];
        var bData = new uint[numWords];
        aData[numWords - 1] = 0x80000000u;
        bData[0] = 1u;

        var expectedWords = new uint[numWords];
        Array.Fill(expectedWords, 0xFFFFFFFFu);
        expectedWords[numWords - 1] = 0x7FFFFFFFu;

        var resultWarpKs = new uint[numWords];
        var resultWarpBk = new uint[numWords];
        var resultBlockBk = new uint[numWords];

        ParallelAdder.LaunchWarpParallelSubKernel(aData, bData, resultWarpKs, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchWarpBrentKungParallelSubKernel(aData, bData, resultWarpBk, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchBrentKungParallelSubKernel(aData, bData, resultBlockBk, numWords, gridDimX: 1, blockDimX: 256);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultWarpKs));
        Assert.True(expectedWords.AsSpan().SequenceEqual(resultWarpBk));
        Assert.True(expectedWords.AsSpan().SequenceEqual(resultBlockBk));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(129)]
    [InlineData(255)]
    [InlineData(257)]
    public void NonMultipleOfWarpSize_AddAndSub_MatchBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        // Also allocate extra trailing elements to test explicit numWords < array.Length
        const int Padding = 5;
        var (aRaw, bRaw, expectedAdd) = CreateRandomTestVectors(numWords, sign: 1, seed: 999);
        var aPadded = new uint[numWords + Padding];
        var bPadded = new uint[numWords + Padding];
        Array.Copy(aRaw, aPadded, numWords);
        Array.Copy(bRaw, bPadded, numWords);
        for (int i = numWords; i < numWords + Padding; i++)
        {
            aPadded[i] = 0xCAFEBABEu;
            bPadded[i] = 0x12345678u;
        }

        var resWarpKsAdd = new uint[numWords + Padding];
        var resBlockBkAdd = new uint[numWords + Padding];
        ParallelAdder.LaunchWarpParallelAddKernel(aPadded, bPadded, resWarpKsAdd, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchBrentKungParallelAddKernel(aPadded, bPadded, resBlockBkAdd, numWords, gridDimX: 1, blockDimX: 256);

        Assert.True(expectedAdd.AsSpan().SequenceEqual(resWarpKsAdd.AsSpan(0, numWords)));
        Assert.True(expectedAdd.AsSpan().SequenceEqual(resBlockBkAdd.AsSpan(0, numWords)));

        var (aSub, bSub, expectedSub) = CreateRandomTestVectors(numWords, sign: -1, seed: 888);
        var resWarpKsSub = new uint[numWords];
        var resBlockBkSub = new uint[numWords];
        ParallelAdder.LaunchWarpParallelSubKernel(aSub, bSub, resWarpKsSub, numWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchBrentKungParallelSubKernel(aSub, bSub, resBlockBkSub, numWords, gridDimX: 1, blockDimX: 256);

        Assert.True(expectedSub.AsSpan().SequenceEqual(resWarpKsSub));
        Assert.True(expectedSub.AsSpan().SequenceEqual(resBlockBkSub));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void AlternatingBitPatterns_WithCarryPropagation_MatchesBigInteger()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 96; // 3 full warps
        var aData = new uint[NumWords];
        var bData = new uint[NumWords];
        for (int i = 0; i < NumWords; i++)
        {
            aData[i] = 0x55555555u;
            bData[i] = 0xAAAAAAAAu; // 0x55555555 + 0xAAAAAAAA = 0xFFFFFFFF (Propagate = 1, Generate = 0)
        }

        // Trigger carry generation at word 0 so it propagates through all 96 words of alternating bits
        bData[0] = 0xAAAAAAABu;

        var expectedWords = new uint[NumWords];
        ComputeExpected(aData, bData, NumWords, sign: 1, expectedWords);

        var resultWarpKs = new uint[NumWords];
        var resultWarpBk = new uint[NumWords];
        var resultBlockBk = new uint[NumWords];

        ParallelAdder.LaunchWarpParallelAddKernel(aData, bData, resultWarpKs, NumWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchWarpBrentKungParallelAddKernel(aData, bData, resultWarpBk, NumWords, gridDimX: 1, blockDimX: 32);
        ParallelAdder.LaunchBrentKungParallelAddKernel(aData, bData, resultBlockBk, NumWords, gridDimX: 1, blockDimX: 256);

        Assert.True(expectedWords.AsSpan().SequenceEqual(resultWarpKs));
        Assert.True(expectedWords.AsSpan().SequenceEqual(resultWarpBk));
        Assert.True(expectedWords.AsSpan().SequenceEqual(resultBlockBk));
    }
}
