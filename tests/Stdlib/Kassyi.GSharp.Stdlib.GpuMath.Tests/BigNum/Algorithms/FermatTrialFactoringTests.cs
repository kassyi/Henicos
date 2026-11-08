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
            results: resultsBuffer.ToProxy(),
            resultCount: resultCountBuffer.ToProxy(),
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

    // startOdd = 1 makes thread 0 test k = 1, whose exponent reduction is exp % 0; the top of the range makes the
    // candidates of the later threads wrap past 2^32 - 1 to 1, 3, 5, ... Every reported k must be an odd candidate of
    // the launch in [3, 2^32 - 1] that divides N(exp) = 2^(2 exp + 1) - 2^exp + 11.
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1u, 5u)]
    [InlineData(1u, 6u)]
    [InlineData(0xFFFFFFF1u, 5u)]
    [InlineData(0xFFFFFFF1u, 6u)]
    public void SearchFactors32Kernel_CandidatesBelowThreeOrPastUInt32_AreNotTested(uint startOdd, uint exp)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        const int MaxResults = 64;
        const int Threads = 32;
        using var resultsBuffer = new GpuBuffer<uint>(MaxResults);
        using var resultCountBuffer = new GpuBuffer<uint>(1);
        uint[] count = [0u];
        resultCountBuffer.CopyToDevice(count);

        // mutant: FermatTrialFactoringKernels.cu.cs SearchFactors32Kernel `candidate < 3UL ||` removed (reports 1)
        // mutant: FermatTrialFactoringKernels.cu.cs SearchFactors32Kernel `|| candidate > 0xFFFFFFFFUL` removed
        // (exp = 6 reports the wrapped candidate 3)
        FermatTrialFactoringKernels.LaunchSearchFactors32KernelPtr(
            startOdd: startOdd,
            exp: exp,
            results: resultsBuffer.ToProxy(),
            resultCount: resultCountBuffer.ToProxy(),
            maxResults: MaxResults,
            gridDimX: 1,
            blockDimX: Threads);
        GpuKernelLoader.SynchronizeDevice();

        resultCountBuffer.CopyToHost(count);
        var found = new uint[MaxResults];
        resultsBuffer.CopyToHost(found);

        var n = (BigInteger.One << (int)(2 * exp + 1)) - (BigInteger.One << (int)exp) + 11;
        var expected = new List<uint>();
        for (var t = 0UL; t < Threads; t++)
        {
            var k = startOdd + 2UL * t;
            if (k >= 3UL && k <= uint.MaxValue && (n % k).IsZero)
            {
                expected.Add((uint)k);
            }
        }

        var reported = found[..(int)Math.Min(count[0], MaxResults)];
        Array.Sort(reported);
        Assert.Equal(expected, reported);
    }

    // An exponent at or above k - 1. Reducing it modulo k - 1 (Fermat's little theorem) holds only for a prime k: the
    // composite 186049687 = 67 * 661 * 4201 passes the sieve, and 2^(exp mod (k - 1)) mod k makes N(exp) look divisible
    // by it although N(exp) mod k = 180603033. It is the candidate of thread 0 here. Every reported k must be a
    // candidate that passes the sieve and divides N(exp), and every such candidate must be reported.
    // mutant: FermatTrialFactoringKernels.cu.cs SearchFactors32Kernel `ModPow(2UL, exp, k)` ->
    // `ModPow(2UL, exp % (k32 - 1u), k)` (reports 186049687; input: startOdd 186049687)
    // mutant: FermatTrialFactoringKernels.cu.cs SearchFactors32Kernel `(x * x) % k` -> `x % k` (input: startOdd 3)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(186049687u, 1u, 32u)]
    [InlineData(3u, 8u, 256u)]
    public void SearchFactors32Kernel_ExponentAboveKMinusOne_ReportsExactlyTheDivisors(
        uint startOdd,
        uint gridDimX,
        uint blockDimX)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        const uint Exp = 2740652584u;
        const int MaxResults = 64;
        using var resultsBuffer = new GpuBuffer<uint>(MaxResults);
        using var resultCountBuffer = new GpuBuffer<uint>(1);
        uint[] count = [0u];
        resultCountBuffer.CopyToDevice(count);

        FermatTrialFactoringKernels.SearchFactors32(
            startOdd,
            Exp,
            resultsBuffer.ToProxy(),
            resultCountBuffer.ToProxy(),
            MaxResults,
            gridDimX,
            blockDimX);
        GpuKernelLoader.SynchronizeDevice();

        resultCountBuffer.CopyToHost(count);
        var found = new uint[MaxResults];
        resultsBuffer.CopyToHost(found);

        var expected = ExpectedFactors(startOdd, gridDimX * blockDimX, Exp);
        // Witness of the second row: the window holds divisors (3 and 13), so a check that reports nothing fails.
        Assert.True(startOdd != 3u || expected.Count > 0);
        var reported = found[..(int)Math.Min(count[0], MaxResults)];
        Array.Sort(reported);
        Assert.Equal(expected, reported);
    }

    // The kernel stores nothing for maxResults <= 0, whatever it finds: a negative count converted to uint would
    // otherwise allow every index. The count still says how many factors were found.
    // mutant: FermatTrialFactoringKernels.cu.cs SearchFactors32Kernel `maxResults > 0 &&` removed (rows -1, MinValue)
    // mutant: `idx < (uint)maxResults` -> `idx <= (uint)maxResults` (row 1 writes word 1; row 0 writes word 0)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(1)]
    public void SearchFactors32Kernel_StoresAtMostMaxResultsAndNothingForNonPositive(int maxResults)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        const uint Exp = 14u;
        const int CanaryWords = 64;
        const uint Canary = 0xDEADBEEFu;
        using var resultsBuffer = new GpuBuffer<uint>(CanaryWords);
        using var resultCountBuffer = new GpuBuffer<uint>(1);
        var words = new uint[CanaryWords];
        Array.Fill(words, Canary);
        resultsBuffer.CopyToDevice(words);
        uint[] count = [0u];
        resultCountBuffer.CopyToDevice(count);

        // The kernel launcher itself, not the checked host entry point: the guard is in the kernel.
        FermatTrialFactoringKernels.LaunchSearchFactors32KernelPtr(
            startOdd: 3u,
            exp: Exp,
            results: resultsBuffer.ToProxy(),
            resultCount: resultCountBuffer.ToProxy(),
            maxResults: maxResults,
            gridDimX: 8,
            blockDimX: 256);
        GpuKernelLoader.SynchronizeDevice();

        resultCountBuffer.CopyToHost(count);
        resultsBuffer.CopyToHost(words);

        var expected = ExpectedFactors(3u, 8 * 256, Exp);
        Assert.Equal((uint)expected.Count, count[0]);
        var stored = Math.Max(maxResults, 0);
        for (var i = 0; i < stored; i++)
        {
            Assert.Contains(words[i], expected);
        }

        for (var i = stored; i < CanaryWords; i++)
        {
            Assert.Equal(Canary, words[i]);
        }
    }

    // The host entry point rejects a negative maxResults before the launch, as the Span APIs reject a negative
    // length, and an array entry point also rejects a maxResults past the end of results.
    // mutant: FermatTrialFactoringKernels.Host.cs `ArgumentOutOfRangeException.ThrowIfNegative(maxResults)` removed
    // mutant: FermatTrialFactoringKernels.Host.cs `ThrowIfGreaterThan(maxResults, results.Length)` removed
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void SearchFactors32_InvalidMaxResults_ThrowsBeforeTheLaunch()
    {
        uint[] results = [0u];
        uint[] count = [0u];

        var negative = Assert.Throws<ArgumentOutOfRangeException>(() =>
            FermatTrialFactoringKernels.SearchFactors32(3u, 14u, results, count, -1, 8, 256));
        Assert.Equal("maxResults", negative.ParamName);

        var tooMany = Assert.Throws<ArgumentOutOfRangeException>(() =>
            FermatTrialFactoringKernels.SearchFactors32(3u, 14u, results, count, 2, 8, 256));
        Assert.Equal("maxResults", tooMany.ParamName);

        var pointer = Assert.Throws<ArgumentOutOfRangeException>(() =>
            FermatTrialFactoringKernels.SearchFactors32(3u, 14u, default(GSharpArrayProxy), default, int.MinValue, 8, 256));
        Assert.Equal("maxResults", pointer.ParamName);

        // The device buffers carry their lengths, so the pointer entry point rejects too many results as well
        var pointerTooMany = Assert.Throws<ArgumentOutOfRangeException>(() =>
            FermatTrialFactoringKernels.SearchFactors32(
                3u, 14u, new GSharpArrayProxy(new IntPtr(0x1000), 1), new GSharpArrayProxy(new IntPtr(0x2000), 1), 2, 8, 256));
        Assert.Equal("maxResults", pointerTooMany.ParamName);

        // Nothing ran: the count is untouched.
        Assert.Equal(0u, count[0]);
    }

    // The array entry point validates and launches: with room for every factor, the stored factors are the divisors.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void SearchFactors32_ArrayEntryPoint_StoresTheDivisors()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();

        var results = new uint[16];
        uint[] count = [0u];
        FermatTrialFactoringKernels.SearchFactors32(3u, 14u, results, count, results.Length, 8, 256);

        var expected = ExpectedFactors(3u, 8 * 256, 14u);
        var reported = results[..(int)count[0]];
        Array.Sort(reported);
        Assert.Equal(expected, reported);
    }

    // The odd candidates startOdd + 2t of the launch in [3, 2^32 - 1] that pass the sieve and divide
    // N(exp) = 2^(2 exp + 1) - 2^exp + 11, computed with BigInteger from the exponent itself (no reduction).
    private static List<uint> ExpectedFactors(uint startOdd, uint threads, uint exp)
    {
        var expected = new List<uint>();
        for (var t = 0UL; t < threads; t++)
        {
            var k = startOdd + 2UL * t;
            if (k < 3UL || k > uint.MaxValue || !FermatTrialFactoringKernels.PassesQuadraticAndSmallPrimeSieve(k))
            {
                continue;
            }

            var x = BigInteger.ModPow(2, exp, k);
            if (((2 * x * x - x + 11) % k).IsZero)
            {
                expected.Add((uint)k);
            }
        }

        return expected;
    }

    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GoldilocksArithmetic_Reduce128AndModularOps_MatchBigIntegerReference()
    {
        BigInteger p = new BigInteger(GoldilocksArithmetic.GoldilocksPrime);
        ulong[] testValues =
        [
            0UL,
            1UL,
            2UL,
            0xFFFFFFFFUL,
            0x100000000UL,
            GoldilocksArithmetic.GoldilocksTwoAdicRoot,
            GoldilocksArithmetic.GoldilocksPrime - 2UL,
            GoldilocksArithmetic.GoldilocksPrime - 1UL
        ];

        foreach (ulong a in testValues)
        {
            foreach (ulong b in testValues)
            {
                ulong expectedAdd = (ulong)((new BigInteger(a) + new BigInteger(b)) % p);
                ulong expectedSub = (ulong)(((new BigInteger(a) - new BigInteger(b)) % p + p) % p);
                ulong expectedMul = (ulong)((new BigInteger(a) * new BigInteger(b)) % p);

                Assert.Equal(expectedAdd, GoldilocksArithmetic.GoldilocksAdd(a, b));
                Assert.Equal(expectedSub, GoldilocksArithmetic.GoldilocksSub(a, b));
                Assert.Equal(expectedMul, GoldilocksArithmetic.GoldilocksMul(a, b));
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
