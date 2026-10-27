namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// GPU tests of <see cref="FermatAlgorithm"/>'s device stage (<c>E &gt;= 32</c>): NTT squaring with an owned scratch, the
/// <c>QuadraticFoldReduceKernel</c> fold and the CUDA Graph cache. Expected values come from <see cref="BigInteger"/>:
/// after <c>k</c> squarings of the base <c>a</c> the state is <c>a^(2^k) mod N</c>, <c>N = 2^(2E+1) - 2^E + 11</c>.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmTests
{
    // E = 32 is the first exponent of the device stage (3 words). The others put m = 2E + 1 next to a word boundary
    // (m = 32j +- 1) and E % 32 at 0 / 31 / 1, so the fold's shifts by m and by E take every branch, including the
    // whole-word shift (E = 64, 96) that must not shift a word by 32 bits.
    public static TheoryData<int, uint> DeviceExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 32, 33, 47, 48, 63, 64, 65, 95, 96 })
            {
                data.Add(e, 2u);
                data.Add(e, 3u);
            }

            return data;
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(DeviceExponents))]
    public void RunFermatPrp_DeviceStage_MatchesBigInteger(int e, uint @base)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e, @base);

        var (result, elapsedMs) = runner.RunFermatPrp();
        Assert.True(elapsedMs >= 0);

        // P-ref: E squarings give a^(2^E) mod N, returned canonical in L = ceil((2E+1)/32) words.
        // mutant: GpuBigIntFermatKernels.cu.cs QuadraticFoldPass `runningH = 2u` -> `3u`
        // mutant: GpuBigIntFermatKernels.cu.cs GetQuotientWord `index < 0 ? 0u :` removed
        // (The E % 32 == 0 branch is not killable here: PTX clamps a shift by 32 to 0, so the hardware hides the C++ UB.)
        Assert.Equal((2 * e + 1 + 31) / 32, result.Length);
        Assert.Equal(runner.OutNumWords, result.Length);
        Assert.Equal(Squarings(e, @base, e), FromWords(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RunBenchmarkChunk_GraphLengths_EachRunTheRequestedSquarings()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 257;
        const uint Base = 3;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E, Base);

        // 1: the eager warm-up only; 256: one full-length graph; 257: the full graph again plus a 1-squaring graph;
        // 100 twice: a 100-squaring graph captured once and replayed; 3: one more length. Each chunk must advance the
        // chain by exactly its length, so a graph cached under the wrong length shows as a wrong residue.
        // mutant: FermatAlgorithm.cs GetOrCaptureGraph `_graphs.TryGetValue(length, ...)` -> `_graphs.TryGetValue(MaxGraphLength, ...)`
        // mutant: FermatAlgorithm.cs SquareState `count--` after the warm-up removed
        var k = 0;
        foreach (var chunk in new[] { 1, 256, 257, 100, 100, 3, 0 })
        {
            runner.RunBenchmarkChunk(chunk);
            k += chunk;

            var state = runner.GetState();
            Assert.True(Squarings(E, Base, k) == FromWords(state), $"after {k} squarings (chunk {chunk})");
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(40, 2u)]
    [InlineData(41, 3u)]
    public void RunFermatPrpWithGerbiczCheck_VerifiesAndMatchesBigInteger(int e, uint @base)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e, @base);

        var (result, _, checksPassed, rollbacks) = runner.RunFermatPrpWithGerbiczCheck(blockSize: 10, checkStepBlocks: 2);

        // The check multiplies through the provider and squares through the owned scratch; every check passes.
        // mutant: FermatAlgorithm.cs SquareState graph cache key -> MaxGraphLength (block lengths 10 and 1 reuse one graph)
        // (Whether the check can fail is not observable without fault injection: stage 4, G2 / G3.)
        Assert.True(checksPassed >= 2);
        Assert.Equal(0, rollbacks);
        Assert.Equal(Squarings(e, @base, e), FromWords(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LoadState_ResidueFromAnotherInstance_ContinuesTheChain()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 40;
        const uint Base = 3;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var first = new FermatAlgorithm(E, Base);
        using var second = new FermatAlgorithm(E, Base);

        first.RunBenchmarkChunk(5);
        second.LoadState(first.GetState());
        second.RunBenchmarkChunk(7);

        // The saved residue is the whole state: 5 + 7 squarings.
        Assert.Equal(Squarings(E, Base, 12), FromWords(second.GetState()));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LoadState_InvalidResidue_ThrowsAndLeavesTheState()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 40;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E);
        var modulus = ToWords(ModulusOf(E), runner.OutNumWords);

        Assert.Throws<ArgumentNullException>(() => runner.LoadState(null!)); // Intentionally passing null to assert the guard
        Assert.Throws<ArgumentException>(() => runner.LoadState(new uint[runner.OutNumWords + 1]));

        // N itself is not a residue; N - 1 is the largest one.
        // mutant: FermatAlgorithm.cs DeviceResidueEngine.IsBelowModulus `return false` -> `return true` (N accepted)
        Assert.Throws<ArgumentException>(() => runner.LoadState(modulus));
        Assert.Equal(new BigInteger(2), FromWords(runner.GetState()));

        var largest = ToWords(ModulusOf(E) - 1, runner.OutNumWords);
        runner.LoadState(largest);
        Assert.Equal(ModulusOf(E) - 1, FromWords(runner.GetState()));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(257)]
    public void GetState_FoldedSquareAtOrAboveN_IsReturnedCanonical(int e)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e);

        // The fold of (N - 1)^2 is N + 1, a valid redundant state (below 2N) that is not a residue; the read must
        // subtract N and return 1.
        // mutant: FermatAlgorithm.cs DeviceResidueEngine.ReadCanonical `_modulus.Canonicalize(words)` removed
        runner.LoadState(ToWords(ModulusOf(e) - 1, runner.OutNumWords));
        runner.RunBenchmarkChunk(1);

        var expected = new uint[runner.OutNumWords];
        expected[0] = 1;
        Assert.Equal(expected, runner.GetState());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Disposed_DeviceStage_ThrowsObjectDisposedException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var runner = new FermatAlgorithm(40);
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunFermatPrpWithGerbiczCheck(blockSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunFermatPrpWithGerbiczCheck(checkStepBlocks: 0));

        runner.Dispose();
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.GetState());
        Assert.Throws<ObjectDisposedException>(() => runner.RunFermatPrp());
        Assert.Throws<ObjectDisposedException>(() => runner.RunBenchmarkChunk());
        Assert.Throws<ObjectDisposedException>(() => runner.LoadState(new uint[runner.OutNumWords]));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(134_217_728)]
    [InlineData(int.MaxValue)]
    public void Constructor_ExponentOutOfRange_ThrowsBeforeAllocating(int invalidE)
    {
        // Rejected before any workspace exists, so this needs no GPU; above 2^27 - 1 the square exceeds the NTT length.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(invalidE));
        Assert.Equal("e", exception.ParamName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u)]
    [InlineData(1u)]
    public void Constructor_BaseBelowTwo_Throws(uint invalidBase)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(40, invalidBase));
        Assert.Equal("base", exception.ParamName);
    }

    [Fact(Skip =
        "cuMemGetInfo_v2 measures global device VRAM and is extremely flaky on shared GPUs. Use Benchmark CLI to measure leaks instead.")]
    [Scenario(TestScenario.Boundary)]
    public void FermatAlgorithm_VramLeak_Test()
    {
        int e = 40;
        using var scope = new GpuKernelLoader.CudaContextScope();

        // Force JIT compilation overhead before tracking memory
        using (var warmupRunner = new FermatAlgorithm(41))
        {
            warmupRunner.RunBenchmarkChunk(2);
        }

        CudaDriverApi.cuMemGetInfo_v2(out nint vramBefore, out _);

        using (var runner = new FermatAlgorithm(e))
        {
            runner.RunFermatPrp();
        }

        CudaDriverApi.cuMemGetInfo_v2(out nint vramAfter, out _);

        long leakedBytes = vramBefore - vramAfter;
        Assert.True(leakedBytes <= 1024 * 1024, $"VRAM leak detected! Leaked {leakedBytes} Bytes.");
    }
}
