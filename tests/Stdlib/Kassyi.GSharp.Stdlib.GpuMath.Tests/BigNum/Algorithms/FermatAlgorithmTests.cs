#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// GPU tests of <see cref="FermatAlgorithm"/>'s device stage (<c>E &gt;= 32</c>): the PRP verdict and residues, the
/// Gerbicz-Li check, checkpoints, cancellation, the CUDA Graph cache and the canonical readback. References come from
/// <see cref="BigInteger"/>.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmTests
{
    // N is prime for these exponents.
    public static TheoryData<int> PrimeExponents => [51, 61, 87, 157, 205, 293];

    // E = 32 is the first exponent of the device stage (3 words). The others put m = 2E + 1 next to a word boundary
    // (m = 32j +- 1) and E % 32 at 0 / 31 / 1, so the fold's shifts by m and by E take every branch. Base 3 only for odd
    // E: for even E, 3 divides N and no chain runs.
    public static TheoryData<int, uint> BoundaryExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 32, 33, 47, 48, 63, 64, 65, 95, 96 })
            {
                data.Add(e, 2u);
                if (e % 2 == 1)
                {
                    data.Add(e, 3u);
                }
            }

            return data;
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(PrimeExponents))]
    public void Run_PrimeModulus_IsProbablePrime(int e)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e);
        var expected = PrpReference(e, 3);

        var result = runner.Run();

        // P-verdict, P-ref and P-identity, for N prime.
        // mutant: FermatAlgorithm.cs RunCoreAsync `_modulus.Multiply(Final, Final, Constant)` removed (no a^10)
        // mutant: GpuBigIntFermatKernels.cu.cs QuadraticFoldPass `runningH = 2u` -> `3u`
        Assert.True(expected.IsProbablePrime);
        Assert.True(result.IsProbablePrime);
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.CheckResidue64, result.CheckResidue64);
        Assert.Equal(0, result.Rollbacks);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(BoundaryExponents))]
    public void Run_WordBoundaryExponents_MatchBigInteger(int e, uint @base)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e, @base);
        var expected = PrpReference(e, @base);

        var result = runner.Run();

        // P-ref, P-verdict and P-identity, with residues of 3 to 7 words.
        // mutant: GpuBigIntFermatKernels.cu.cs GetQuotientWord `if (index < 0) return 0u;` removed
        // (The E % 32 == 0 branch is not killable here: PTX clamps a shift by 32 to 0, so the hardware hides the C++ UB.)
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.CheckResidue64, result.CheckResidue64);
        Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
        Assert.Equal(result.IsProbablePrime, result.Residue64 == result.CheckResidue64);
        Assert.Equal((2 * e + 1 + 31) / 32, runner.GetState().Length);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(40, 5)]
    [InlineData(45, 5)]
    [InlineData(50, 6)]
    public void Run_BlockSizeTen_ChecksEveryTwentySquaringsAndAtTheEnd(int e, int expectedChecks)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e, 2, new FermatPrpOptions { BlockSize = 10, CheckIntervalBlocks = 2 });

        var result = runner.Run();

        // K' = roundUp(2E + 1, 10): 90, 100, 110. Checks at every multiple of 20 and at K' (4 + 1, 5, 5 + 1).
        // mutant: SquaringChainDriver.cs RunAsync `&& k != EndIteration` removed (no last check unless K' is a multiple of 20)
        var endIteration = (2L * e + 1 + 9) / 10 * 10;
        Assert.Equal(expectedChecks, result.ChecksPassed);
        Assert.Equal(0, result.Rollbacks);
        Assert.Equal(endIteration, result.Iterations);
        Assert.Equal(PrpReference(e, 2).Residue64, result.Residue64);
        Assert.Equal(Squarings(e, 2, endIteration), FromWords(runner.GetState()));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Restore_VerifiedCheckpoints_ResumeToTheSameResult()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        var options = new FermatPrpOptions { BlockSize = 7 };
        using var scope = new GpuKernelLoader.CudaContextScope();

        // B = 7 (odd block, graph length 7), K = 123, K' = 126, checks every 49 squarings: at 49 (before E), 98 (after
        // E) and 126.
        var checkpoints = new List<FermatPrpCheckpoint>();
        FermatPrpResult expected;
        using (var runner = new FermatAlgorithm(E, 3, options with { OnVerifiedCheckpoint = checkpoints.Add }))
        {
            expected = runner.Run();
        }

        Assert.Equal(new long[] { 49, 98, 126 }, checkpoints.Select(static c => c.Iteration));
        Assert.True(checkpoints[0].SnapshotE.IsEmpty);
        Assert.Equal((2 * E + 1 + 31) / 32, checkpoints[1].SnapshotE.Length);
        Assert.True(checkpoints[1].FinalK.IsEmpty);
        Assert.Equal((2 * E + 1 + 31) / 32, checkpoints[2].FinalK.Length);

        // Resuming from before and after the snapshot point, and from the last point (where no squaring is left and the
        // verdict reads the saved u_K), gives the same result as one run.
        // mutant: SquaringChainDriver.cs Load `_modulus.Write(Snapshot, point.Snapshot)` removed (u_E lost after k = 98)
        // mutant: SquaringChainDriver.cs Load `_modulus.Write(Final, point.Final)` removed (u_K lost at k = 126)
        foreach (var checkpoint in checkpoints)
        {
            using var resumed = new FermatAlgorithm(E, 3, options);
            resumed.Restore(checkpoint);
            Assert.Equal(checkpoint.Iteration, resumed.Iteration);

            var result = resumed.Run();

            Assert.Equal(expected.Residue64, result.Residue64);
            Assert.Equal(expected.CheckResidue64, result.CheckResidue64);
            Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
            Assert.Equal(expected.ChecksPassed, result.ChecksPassed);
            Assert.Equal(126 - checkpoint.Iteration, result.Iterations);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Restore_CheckpointOfAnotherTest_Throws()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        var options = new FermatPrpOptions { BlockSize = 7 };
        using var scope = new GpuKernelLoader.CudaContextScope();
        var checkpoints = new List<FermatPrpCheckpoint>();
        using (var source = new FermatAlgorithm(E, 3, options with { OnVerifiedCheckpoint = checkpoints.Add }))
        {
            source.Run();
        }

        var good = checkpoints[1];
        var words = good.State.Length;
        using var runner = new FermatAlgorithm(E, 3, options);

        // Another E, base or block size; a position off the block grid; residues of the wrong length or not below N.
        // mutant: FermatAlgorithm.cs Restore `checkpoint.E != E ||` removed
        Assert.Throws<ArgumentNullException>(() => runner.Restore(null!)); // Intentionally passing null to assert the guard
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, e: 63)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, @base: 5)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, blockSize: 8)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, iteration: 97)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, iteration: 133)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, state: new uint[words + 1])));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, snapshotE: Array.Empty<uint>())));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, finalK: new uint[words])));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(checkpoints[2], finalK: Array.Empty<uint>())));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, checksPassed: -1)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, rollbacks: -1)));
        Assert.Throws<ArgumentException>(() => runner.Restore(With(good, state: ToWords(ModulusOf(E), words))));
        Assert.Equal(0, runner.Iteration);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Restore_DamagedState_FailsEveryCheckAndThrows()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        var options = new FermatPrpOptions { BlockSize = 7 };
        using var scope = new GpuKernelLoader.CudaContextScope();
        var checkpoints = new List<FermatPrpCheckpoint>();
        using (var source = new FermatAlgorithm(E, 3, options with { OnVerifiedCheckpoint = checkpoints.Add }))
        {
            source.Run();
        }

        // A copy damaged in storage is outside the check, but every later check fails, and the rollbacks return to the
        // same damaged point until the run gives up.
        var damaged = checkpoints[0].State.ToArray();
        damaged[0] ^= 1;
        using var runner = new FermatAlgorithm(E, 3, options);
        runner.Restore(With(checkpoints[0], state: damaged));

        var exception = Assert.Throws<InvalidOperationException>(() => runner.Run());
        Assert.Contains("3 times in a row", exception.Message);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RunAsync_CanceledAfterThreeBlocks_ResumesFromTheLastCheck()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        var options = new FermatPrpOptions { BlockSize = 7, CheckIntervalBlocks = 1 };
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var cts = new CancellationTokenSource();
        using var runner = new FermatAlgorithm(E, 3, options);
        var progress = new SynchronousProgress<FermatPrpProgress>(p =>
        {
            if (p.Iteration >= 21)
            {
                cts.Cancel();
            }
        });

        // mutant: SquaringChainDriver.cs RunAsync `if (cancellationToken.IsCancellationRequested)` block removed
        Assert.ThrowsAny<OperationCanceledException>(() =>
            runner.RunAsync(progress, cts.Token).AsTask().GetAwaiter().GetResult());

        // The state is back at the last verified point, k = 21, which the checkpoint holds.
        var checkpoint = runner.CreateCheckpoint();
        Assert.Equal(21, checkpoint.Iteration);
        Assert.Equal(21, runner.Iteration);
        Assert.Equal(Squarings(E, 3, 21), FromWords(runner.GetState()));

        runner.Restore(checkpoint);
        var result = runner.Run();
        Assert.Equal(PrpReference(E, 3).Residue64, result.Residue64);
        Assert.Equal(126 / 7, result.ChecksPassed);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Run_TwiceAndFromTheLastPointOfTheSameInstance_GivesTheSameResult()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { BlockSize = 7 });

        var first = runner.Run();
        var second = runner.Run();
        runner.Restore(runner.CreateCheckpoint());
        var resumed = runner.Run();

        // Running again and resuming from the last point of the same instance decide the same (review B1).
        // mutant: SquaringChainDriver.cs Load `_modulus.Write(Final, point.Final)` removed (the last point has no u_K)
        // (Writing u_K * a^10 back into Final is no longer observable: every resume rewrites Final.)
        Assert.True(first.IsProbablePrime);
        Assert.Equal(first, second with { Elapsed = first.Elapsed });
        Assert.Equal(first.Residue64, resumed.Residue64);
        Assert.True(resumed.IsProbablePrime);
        Assert.Equal(0, resumed.Iterations);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SecondRun_CanceledBeforeItsFirstCheck_ReportsTheStartEverywhere()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 61;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { BlockSize = 7 });
        runner.Run();
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<FermatPrpProgress>(_ => cts.Cancel());

        // Canceled at k = 7, before the first check at 49 (PR #761 review 2).
        Assert.ThrowsAny<OperationCanceledException>(() =>
            runner.RunAsync(progress, cts.Token).AsTask().GetAwaiter().GetResult());

        var checkpoint = runner.CreateCheckpoint();
        Assert.Equal(0, runner.Iteration);
        Assert.Equal(0, checkpoint.Iteration);
        Assert.Equal(checkpoint.State.ToArray(), runner.GetState());
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public async Task RunAsync_MatchesRun()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 51;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E);

        var expected = runner.Run();
        var result = await runner.RunAsync();

        Assert.Equal(expected, result with { Elapsed = expected.Elapsed });
        Assert.Equal(runner.GetState(), await runner.GetStateAsync());
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(33)]
    [InlineData(257)]
    [InlineData(258)]
    [InlineData(513)]
    public void RunBenchmarkChunk_GraphLengths_EachRunTheRequestedSquarings(int e)
    {
        CudaCapability.EnsureGpuAvailable();
        const uint Base = 3;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e, Base);

        // 0: nothing; 1: the eager warm-up only; 17 and 256: graphs of those lengths; 257: the full graph again plus a
        // 1-squaring graph; 100 twice: one graph replayed. A graph cached under the wrong length shows as a wrong state.
        // mutant: QuadraticDeviceModulus.cs GetOrCaptureGraph `TryGetValue(length, ...)` -> `TryGetValue(MaxGraphLength, ...)`
        // mutant: QuadraticDeviceModulus.cs SquareState `count--` after the warm-up removed
        var k = 0;
        foreach (var chunk in new[] { 0, 1, 17, 256, 257, 100, 100 })
        {
            runner.RunBenchmarkChunk(chunk);
            k += chunk;

            var state = runner.GetState();
            Assert.Equal(k, runner.Iteration);
            Assert.True(Squarings(e, Base, k) == FromWords(state), $"after {k} squarings (chunk {chunk})");
        }
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
        using var runner = new FermatAlgorithm(e, 2);
        var words = runner.ResidueWords;

        // The fold of (N - 1)^2 is N + 1, a valid redundant state (below 2N) that is not a residue; the read must
        // subtract N and return 1.
        // mutant: QuadraticDeviceModulus.cs ReadCanonical `_modulus.Canonicalize(words)` removed
        var start = runner.CreateCheckpoint();
        runner.Restore(With(start, state: ToWords(ModulusOf(e) - 1, words)));
        runner.RunBenchmarkChunk(1);

        var expected = new uint[words];
        expected[0] = 1;
        Assert.Equal(expected, runner.GetState());
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Run_AfterBenchmarkChunk_StartsTheChainAgain()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 33;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { BlockSize = 3, CheckIntervalBlocks = 1 });
        runner.RunBenchmarkChunk(1);

        var result = runner.Run();

        // The check multiplies by u_0 = a of the chain it checks (review of stages 1-2).
        // mutant: SquaringChainDriver.cs RunAsync `Multiply(CheckAux, CheckAux, Start)` -> `Multiply(CheckAux, CheckAux, State)`
        Assert.Equal(0, result.Rollbacks);
        Assert.Equal(PrpReference(E, 3).Residue64, result.Residue64);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Run_EvenExponentBaseThree_ReportsTheFactorThree()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(32);

        var result = runner.Run();

        Assert.False(result.IsProbablePrime);
        Assert.Equal(3u, result.SmallDivisor);
        Assert.Equal(0, result.Iterations);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task Disposed_DeviceStage_ThrowsObjectDisposedException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var runner = new FermatAlgorithm(41);
        var checkpoint = runner.CreateCheckpoint();
        runner.Dispose();
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.GetState());
        Assert.Throws<ObjectDisposedException>(() => runner.Run());
        Assert.Throws<ObjectDisposedException>(() => runner.RunBenchmarkChunk());
        Assert.Throws<ObjectDisposedException>(() => runner.CreateCheckpoint());
        Assert.Throws<ObjectDisposedException>(() => runner.Restore(checkpoint));
        Assert.Throws<ObjectDisposedException>(() => runner.GetModulus());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.RunAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.GetStateAsync());
    }

    [Fact(Skip =
        "cuMemGetInfo_v2 measures global device VRAM and is extremely flaky on shared GPUs. Use Benchmark CLI to measure leaks instead.")]
    [Scenario(TestScenario.Boundary)]
    public void FermatAlgorithm_VramLeak_Test()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();

        // Force JIT compilation overhead before tracking memory
        using (var warmupRunner = new FermatAlgorithm(41))
        {
            warmupRunner.RunBenchmarkChunk(2);
        }

        CudaDriverApi.cuMemGetInfo_v2(out nint vramBefore, out _);

        using (var runner = new FermatAlgorithm(41))
        {
            runner.Run();
        }

        CudaDriverApi.cuMemGetInfo_v2(out nint vramAfter, out _);

        long leakedBytes = vramBefore - vramAfter;
        Assert.True(leakedBytes <= 1024 * 1024, $"VRAM leak detected! Leaked {leakedBytes} Bytes.");
    }

    /// <summary>Returns a copy of <paramref name="source"/> with some fields replaced.</summary>
    internal static FermatPrpCheckpoint With(FermatPrpCheckpoint source, int? e = null, uint? @base = null,
        int? blockSize = null, long? iteration = null, uint[]? state = null, uint[]? snapshotE = null,
        uint[]? finalK = null, int? checksPassed = null, int? rollbacks = null) =>
        new(e ?? source.E, @base ?? source.Base, blockSize ?? source.BlockSize, iteration ?? source.Iteration,
            state is null ? source.State : state, source.Check, snapshotE is null ? source.SnapshotE : snapshotE,
            finalK is null ? source.FinalK : finalK, checksPassed ?? source.ChecksPassed,
            rollbacks ?? source.Rollbacks);
}
