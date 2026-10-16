using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class CooperativeMathTests
{
    private static uint ComputeMontgomeryMuHost(uint n0)
    {
        // Newton-Raphson for -n0^{-1} mod 2^32 (requires odd n0)
        uint inv = 1u;
        for (int i = 0; i < 5; i++)
        {
            inv *= 2u - n0 * inv;
        }

        return (uint)-(int)inv;
    }

    private static BigInteger WordsToBigInteger(ReadOnlySpan<uint> words)
    {
        var bytes = new byte[words.Length * 4];
        MemoryMarshal.AsBytes(words).CopyTo(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static void BigIntegerToWords(BigInteger value, Span<uint> dest)
    {
        dest.Clear();
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var byteSpan = MemoryMarshal.AsBytes(dest);
        bytes.AsSpan(0, Math.Min(bytes.Length, byteSpan.Length)).CopyTo(byteSpan);
    }

    private static BigInteger ModInversePowerOfTwo(BigInteger n, int bitCount)
    {
        var r = BigInteger.One << bitCount;
        var mask = r - BigInteger.One;
        BigInteger inv = BigInteger.One;
        for (int bits = 1; bits < bitCount; bits <<= 1)
        {
            inv = (inv * (2 + r - ((n * inv) & mask))) & mask;
        }

        return inv;
    }

    private static BigInteger ComputeMontgomeryReduceBigInteger(BigInteger tBig, BigInteger nBig, int numWords)
    {
        int rBits = numWords * 32;
        var r = BigInteger.One << rBits;
        var mask = r - BigInteger.One;
        // nPrime = -N^{-1} mod R
        var nInvModR = ModInversePowerOfTwo(nBig, rBits);
        var nPrime = (r - nInvModR) & mask;

        var m = ((tBig & mask) * nPrime) & mask;
        var u = (tBig + m * nBig) >> rBits;
        if (u >= nBig)
        {
            u -= nBig;
        }

        // Mathematical invariant check: u * R == T (mod N) and 0 <= u < N
        Assert.True(u >= BigInteger.Zero && u < nBig);
        Assert.Equal(tBig % nBig, (u * r) % nBig);
        return u;
    }

    private static void ComputeExpectedMontgomeryReduce(
        ReadOnlySpan<uint> tWords,
        ReadOnlySpan<uint> modWords,
        int numWords,
        Span<uint> expectedWords)
    {
        var tBig = WordsToBigInteger(tWords.Slice(0, numWords * 2));
        var nBig = WordsToBigInteger(modWords.Slice(0, numWords));
        var expectedBig = ComputeMontgomeryReduceBigInteger(tBig, nBig, numWords);
        BigIntegerToWords(expectedBig, expectedWords);
    }

    private static void ComputeExpectedMontgomeryMultiply(
        ReadOnlySpan<uint> aWords,
        ReadOnlySpan<uint> bWords,
        ReadOnlySpan<uint> modWords,
        int numWords,
        Span<uint> expectedWords)
    {
        var aBig = WordsToBigInteger(aWords.Slice(0, numWords));
        var bBig = WordsToBigInteger(bWords.Slice(0, numWords));
        var nBig = WordsToBigInteger(modWords.Slice(0, numWords));
        var expectedBig = ComputeMontgomeryReduceBigInteger(aBig * bBig, nBig, numWords);
        BigIntegerToWords(expectedBig, expectedWords);
    }

    private static (uint[] T, uint[] Modulus, uint Mu, uint[] Expected) CreateRandomReduceTestVector(
        int numWords,
        int seed = 13579)
    {
        var rnd = new Random(seed + numWords * 37);
        var modWords = new uint[numWords];
        for (int i = 0; i < numWords; i++)
        {
            modWords[i] = (uint)rnd.Next();
        }

        // Ensure odd modulus and non-zero top word
        modWords[0] |= 1u;
        modWords[numWords - 1] |= 0x80000000u;

        var nBig = WordsToBigInteger(modWords);
        var maxT = nBig << (numWords * 32);

        var rawBytes = new byte[numWords * 8];
        rnd.NextBytes(rawBytes);
        var tBig = new BigInteger(rawBytes, isUnsigned: true, isBigEndian: false) % maxT;

        var tWords = new uint[numWords * 2];
        BigIntegerToWords(tBig, tWords);

        uint mu = ComputeMontgomeryMuHost(modWords[0]);
        var expectedWords = new uint[numWords];
        ComputeExpectedMontgomeryReduce(tWords, modWords, numWords, expectedWords);

        return (tWords, modWords, mu, expectedWords);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ComputeMontgomeryMu_ProducesValidNegativeModularInverse()
    {
        uint[] oddValues = [1u, 3u, 5u, 17u, 0x12345679u, 0xFFFFFFFFu, 0x80000001u];
        foreach (uint n0 in oddValues)
        {
            uint mu = CooperativeMath.ComputeMontgomeryMu(n0);
            Assert.Equal(ComputeMontgomeryMuHost(n0), mu);
            // Verify n0 * mu == -1 (0xFFFFFFFF) mod 2^32
            Assert.Equal(0xFFFFFFFFu, unchecked(n0 * mu));
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(4)]   // 128-bit
    [InlineData(8)]   // 256-bit (ECC P-256)
    [InlineData(12)]  // 384-bit (ECC P-384)
    [InlineData(16)]  // 512-bit
    [InlineData(32)]  // 1024-bit
    [InlineData(64)]  // 2048-bit (RSA-2048)
    [InlineData(128)] // 4096-bit (RSA-4096)
    public void MontgomeryReduceWarpSync_MatchesBigIntegerReference(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (tData, modData, mu, expectedWords) = CreateRandomReduceTestVector(numWords);
        var resultData = new uint[numWords];

        CooperativeMath.LaunchWarpMontgomeryReduceKernel(
            tData,
            modData,
            mu,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.Equal(expectedWords, resultData);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(4)]   // 128-bit
    [InlineData(8)]   // 256-bit (ECC P-256)
    [InlineData(12)]  // 384-bit (ECC P-384)
    [InlineData(16)]  // 512-bit
    [InlineData(32)]  // 1024-bit
    [InlineData(64)]  // 2048-bit (RSA-2048)
    [InlineData(128)] // 4096-bit (RSA-4096)
    public void MontgomeryMultiplyWarpSync_MatchesBigIntegerReference(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (_, modData, mu, _) = CreateRandomReduceTestVector(numWords, seed: 24680);
        var nBig = WordsToBigInteger(modData);

        var rnd = new Random(97531 + numWords);
        var aBytes = new byte[numWords * 4];
        var bBytes = new byte[numWords * 4];
        rnd.NextBytes(aBytes);
        rnd.NextBytes(bBytes);

        var aBig = new BigInteger(aBytes, isUnsigned: true, isBigEndian: false) % nBig;
        var bBig = new BigInteger(bBytes, isUnsigned: true, isBigEndian: false) % nBig;

        var aData = new uint[numWords];
        var bData = new uint[numWords];
        BigIntegerToWords(aBig, aData);
        BigIntegerToWords(bBig, bData);

        var expectedWords = new uint[numWords];
        ComputeExpectedMontgomeryMultiply(aData, bData, modData, numWords, expectedWords);

        var resultData = new uint[numWords];
        CooperativeMath.LaunchWarpMontgomeryMultiplyKernel(
            aData,
            bData,
            modData,
            mu,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.Equal(expectedWords, resultData);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void MontgomeryReduceWarpSync_ZeroInput_ReturnsZero(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var (_, modData, mu, _) = CreateRandomReduceTestVector(numWords);
        var tData = new uint[numWords * 2];
        var resultData = new uint[numWords];
        Array.Fill(resultData, 0xDEADBEEFu);

        CooperativeMath.LaunchWarpMontgomeryReduceKernel(
            tData,
            modData,
            mu,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        for (int i = 0; i < numWords; i++)
        {
            Assert.Equal(0u, resultData[i]);
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void MontgomeryReduceWarpSync_MaxBoundTriggeringFinalSubtraction_MatchesBigInteger(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        // Construct modulus N = 2^(32*k) - 1 (all 0xFFFFFFFF), so mu = 1
        var modData = new uint[numWords];
        Array.Fill(modData, 0xFFFFFFFFu);
        uint mu = ComputeMontgomeryMuHost(modData[0]);

        // Construct T = N * R - 1, the maximum valid input to REDC which forces top-level carry / conditional subtraction
        var nBig = WordsToBigInteger(modData);
        var rBig = BigInteger.One << (numWords * 32);
        var tBig = nBig * rBig - BigInteger.One;

        var tData = new uint[numWords * 2];
        BigIntegerToWords(tBig, tData);

        var expectedWords = new uint[numWords];
        ComputeExpectedMontgomeryReduce(tData, modData, numWords, expectedWords);

        var resultData = new uint[numWords];
        CooperativeMath.LaunchWarpMontgomeryReduceKernel(
            tData,
            modData,
            mu,
            resultData,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.Equal(expectedWords, resultData);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(63)]
    public void MontgomeryReduceWarpSync_WithExplicitWordCountAndPadding_DoesNotCorruptTrailingBuffer(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        const int Padding = 4;
        var (tRaw, modRaw, mu, expectedWords) = CreateRandomReduceTestVector(numWords, seed: 777);

        var tPadded = new uint[numWords * 2 + Padding];
        var modPadded = new uint[numWords + Padding];
        var resultPadded = new uint[numWords + Padding];

        Array.Copy(tRaw, tPadded, numWords * 2);
        Array.Copy(modRaw, modPadded, numWords);
        for (int i = 0; i < Padding; i++)
        {
            tPadded[numWords * 2 + i] = 0xCAFEBABEu;
            modPadded[numWords + i] = 0x12345678u;
            resultPadded[numWords + i] = 0xA5A5A5A5u;
        }

        CooperativeMath.LaunchWarpMontgomeryReduceKernel(
            tPadded,
            modPadded,
            mu,
            resultPadded,
            numWords,
            gridDimX: 1,
            blockDimX: 32);

        Assert.Equal(expectedWords, resultPadded.AsSpan(0, numWords).ToArray());
        for (int i = 0; i < Padding; i++)
        {
            Assert.Equal(0xA5A5A5A5u, resultPadded[numWords + i]);
        }
    }
}
