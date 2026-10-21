namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection("GpuWorkspace")]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FermatTrialFactoringTests
{
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void PassesQuadraticAndSmallPrimeSieve_MatchesExactQuadraticReciprocity_ForPrimesUpTo5000()
    {
        for (ulong p = 3; p < 5000; p += 2)
        {
            if (!IsPrimeCpu(p))
            {
                continue;
            }

            bool hasQuadraticRoot = false;
            for (ulong x = 0; x < p; x++)
            {
                if ((2UL * x * x + p - x + 11UL) % p == 0UL)
                {
                    hasQuadraticRoot = true;
                    break;
                }
            }

            bool sieveResult = FermatTrialFactoringKernels.PassesQuadraticAndSmallPrimeSieve(p);
            Assert.Equal(hasQuadraticRoot, sieveResult);
        }
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void PassesQuadraticAndSmallPrimeSieve_RejectsCompositeMultiplesOfSmallPrimes()
    {
        ulong[] smallPrimes = [3UL, 5UL, 7UL, 11UL, 13UL, 17UL, 19UL, 23UL, 29UL];
        foreach (ulong p in smallPrimes)
        {
            for (ulong mult = 3; mult <= 25; mult += 2)
            {
                ulong composite = p * mult;
                if (composite > 29UL)
                {
                    Assert.False(FermatTrialFactoringKernels.PassesQuadraticAndSmallPrimeSieve(composite));
                }
            }
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(3, 131u)]
    [InlineData(4, 13u)]
    [InlineData(6, 3u)]
    [InlineData(6, 2713u)]
    public void SearchFactors32Kernel_FindsKnownPrimeFactors(int exp, uint expectedFactor)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        const int MaxResults = 64;
        using var resultsBuffer = new GpuBuffer<uint>(MaxResults);
        using var resultCountBuffer = new GpuBuffer<uint>(1);

        uint[] zeroCount = [0u];
        resultCountBuffer.CopyToDevice(zeroCount);

        FermatTrialFactoringKernels.LaunchSearchFactors32KernelPtr(
            startOdd: 3u,
            exp: (uint)exp,
            results: resultsBuffer.DevicePointer,
            resultCount: resultCountBuffer.DevicePointer,
            maxResults: MaxResults,
            gridDimX: 8,
            blockDimX: 256);

        GpuKernelLoader.SynchronizeDevice();

        resultCountBuffer.CopyToHost(zeroCount);
        Assert.True(zeroCount[0] > 0u);

        var found = new uint[zeroCount[0]];
        resultsBuffer.CopyToHost(found);
        Assert.Contains(expectedFactor, found);
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GoldilocksArithmetic_Reduce128AndModularOps_MatchBigIntegerReference()
    {
        BigInteger p = new BigInteger(GpuBigIntNttKernels.GoldilocksPrime);
        ulong[] testValues =
        [
            0UL,
            1UL,
            2UL,
            0xFFFFFFFFUL,
            0x100000000UL,
            GpuBigIntNttKernels.GoldilocksTwoAdicRoot,
            GpuBigIntNttKernels.GoldilocksPrime - 2UL,
            GpuBigIntNttKernels.GoldilocksPrime - 1UL
        ];

        foreach (ulong a in testValues)
        {
            foreach (ulong b in testValues)
            {
                ulong expectedAdd = (ulong)((new BigInteger(a) + new BigInteger(b)) % p);
                ulong expectedSub = (ulong)(((new BigInteger(a) - new BigInteger(b)) % p + p) % p);
                ulong expectedMul = (ulong)((new BigInteger(a) * new BigInteger(b)) % p);

                Assert.Equal(expectedAdd, GpuBigIntNttKernels.GoldilocksAdd(a, b));
                Assert.Equal(expectedSub, GpuBigIntNttKernels.GoldilocksSub(a, b));
                Assert.Equal(expectedMul, GpuBigIntNttKernels.GoldilocksMul(a, b));
            }
        }
    }

    private static bool IsPrimeCpu(ulong n)
    {
        if (n < 2UL)
        {
            return false;
        }

        if (n % 2UL == 0UL)
        {
            return n == 2UL;
        }

        for (ulong d = 3UL; d * d <= n; d += 2UL)
        {
            if (n % d == 0UL)
            {
                return false;
            }
        }

        return true;
    }
}
