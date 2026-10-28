namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Tests of <see cref="FermatAlgorithm"/>'s host stage (<c>E &lt;= 31</c>, <c>N &lt; 2^64</c>), which uses no GPU, and of
/// the argument checks that come before any allocation. References come from <see cref="BigInteger"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmHostStageTests
{
    // N is prime for E = 1, 3, 5, 11, 31 and composite for 7, 9, 13; E <= 3 is where the GPU fold is undefined
    // (c = 2^E - 11 <= 0, #532-1) and E = 31 is the last host exponent (N has 63 bits).
    public static TheoryData<int, uint> HostExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 1, 3, 5, 7, 9, 11, 13, 31 })
            {
                data.Add(e, 2u);
                data.Add(e, 3u);
            }

            return data;
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(HostExponents))]
    public void Run_HostStage_MatchesBigInteger(int e, uint @base)
    {
        using var runner = new FermatAlgorithm(e, @base);
        var expected = PrpReference(e, @base);

        var result = runner.Run();

        // P-verdict: the verdict is a^(N-1) = 1 (mod N).
        // P-ref: the residues are a^(2^(2E+1)+10) and a^(2^E) modulo N.
        // P-identity: the verdict is the equality of the two residues.
        // mutant: QuadraticHostModulus.cs MultiplyMod `% _modulus` removed (the low 64 bits of the square)
        // mutant: FermatAlgorithm.cs RunCoreAsync `_modulus.Multiply(Final, Final, Constant)` removed (no a^10)
        Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.CheckResidue64, result.CheckResidue64);
        Assert.Equal(result.IsProbablePrime, result.Residue64 == result.CheckResidue64);
        Assert.Null(result.SmallDivisor);
        Assert.Equal(0, result.Rollbacks);
        Assert.True(result.ChecksPassed >= 1);
        Assert.Equal((2L * e + 1 + runner.BlockSize - 1) / runner.BlockSize * runner.BlockSize, result.Iterations);
        Assert.Equal(result.Iterations, runner.Iteration);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    public void Run_EvenExponentBaseThree_ReportsTheFactorThreeWithoutSquaring(int e)
    {
        // For even E, 2^E = 1 (mod 3) and N = 2 - 1 + 11 = 0 (mod 3): composite with the divisor 3, no chain needed.
        // mutant: FermatAlgorithm.cs RunCoreAsync `_smallDivisor is { } divisor` branch removed
        using var runner = new FermatAlgorithm(e);

        var result = runner.Run();

        Assert.False(result.IsProbablePrime);
        Assert.Equal(3u, result.SmallDivisor);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(0, runner.Iteration);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SmallDivisor_AllocatesNoChainUntilAMemberNeedsIt()
    {
        // E = 16, base 3: 3 divides N, so Run answers without the chain; no residue register is created for it
        // (PR #761 review 1). A member that needs the state creates the chain then.
        // mutant: FermatAlgorithm.cs constructor `if (_smallDivisor is null)` before EnsureChain removed (always eager)
        const int E = 16;
        using var runner = new FermatAlgorithm(E);
        Assert.False(runner.HasChain);

        var result = runner.Run();
        Assert.Equal(3u, result.SmallDivisor);
        Assert.Equal(ModulusOf(E), runner.GetModulus());
        Assert.Equal(0, runner.CreateCheckpoint().Iteration);
        Assert.Equal(new BigInteger(3), FromWords(runner.CreateCheckpoint().State.Span));
        Assert.False(runner.HasChain);

        // The benchmark path still runs (design F3: even E is measured with RunBenchmarkChunk).
        runner.RunBenchmarkChunk(2);
        Assert.True(runner.HasChain);
        Assert.Equal(Squarings(E, 3, 2), FromWords(runner.GetState()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NoSmallDivisor_CreatesTheChainInTheConstructor()
    {
        // Allocation failures still surface from the constructor when the chain is needed.
        using var runner = new FermatAlgorithm(13);

        Assert.True(runner.HasChain);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16u)]
    [InlineData(17u)]
    [InlineData(18u)]
    [InlineData(1u)]
    [InlineData(0u)]
    public void Constructor_TrivialBaseForSmallModulus_Throws(uint @base)
    {
        // E = 1: N = 17. Bases 16 (N - 1), 17 (0) and 18 (1) say nothing about N, nor do 0 and 1.
        // mutant: FermatAlgorithm.cs ThrowIfTrivialBase `|| reduced == n - 1` removed
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(1, @base));
        Assert.Equal("base", exception.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RunBenchmarkChunk_ExponentThree_IsTheValueTheIssueExpects()
    {
        // #532-1: N = 2^7 - 2^3 + 11 = 131 and 2^(2^3) mod 131 = 125.
        using var runner = new FermatAlgorithm(3, 2);

        runner.RunBenchmarkChunk(3);

        Assert.Equal(new[] { 125u }, runner.GetState());
        Assert.Equal(3, runner.Iteration);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(13)]
    [InlineData(31)]
    public void Run_AfterBenchmarkChunk_StartsTheChainAgainAndPassesItsChecks(int e)
    {
        // The chain starts from u_0 = a whatever the state is; the Gerbicz-Li check multiplies by that u_0 (review of
        // stages 1-2: the old check used the base against a chain that started elsewhere and always failed).
        // mutant: SquaringChainDriver.cs RunAsync `Multiply(CheckAux, CheckAux, Start)` -> `Multiply(CheckAux, CheckAux, State)`
        using var runner = new FermatAlgorithm(e, 3, new FermatPrpOptions { BlockSize = 3, CheckIntervalBlocks = 1 });
        runner.RunBenchmarkChunk(1);

        var result = runner.Run();
        var again = runner.Run();

        var expected = PrpReference(e, 3);
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
        Assert.Equal(0, result.Rollbacks);
        Assert.Equal(result, again with { Elapsed = result.Elapsed });
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(31)]
    public void Restore_EveryVerifiedPoint_ResumesToTheSameResult(int e)
    {
        // B = 7: for E = 5 and 13 the last point K' is past K = 2E + 1 and carries u_K; for E = 31, K = 63 is a block
        // boundary, so the last point is u_K itself and Load copies it from the state.
        // E = 21 is itself a block boundary: the point at k = E holds u_E as its state, and Load copies it.
        // mutant: SquaringChainDriver.cs Load `_modulus.Copy(Final, State)` for Iteration == ChainLength removed (E = 31)
        // mutant: SquaringChainDriver.cs Load `_modulus.Copy(Snapshot, State)` for Iteration == SnapshotAt removed (E = 21)
        var options = new FermatPrpOptions { BlockSize = 7, CheckIntervalBlocks = 1 };
        var checkpoints = new List<FermatPrpCheckpoint>();
        FermatPrpResult expected;
        using (var source = new FermatAlgorithm(e, 3, options with { OnVerifiedCheckpoint = checkpoints.Add }))
        {
            expected = source.Run();
        }

        Assert.Equal(checkpoints[^1].Iteration > 2L * e + 1, !checkpoints[^1].FinalK.IsEmpty);
        foreach (var checkpoint in checkpoints)
        {
            using var resumed = new FermatAlgorithm(e, 3, options);
            resumed.Restore(checkpoint);

            var result = resumed.Run();

            Assert.True(expected.Residue64 == result.Residue64, $"E={e}, resumed from {checkpoint.Iteration}");
            Assert.True(expected.CheckResidue64 == result.CheckResidue64, $"E={e}, u_E after resuming from {checkpoint.Iteration}");
            Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HostStage_LastExponent_HoldsTwoWordResidues()
    {
        // E = 31: N has 63 bits, so a residue needs both words and its square needs UInt128.
        // mutant: QuadraticHostModulus.cs MaxExponent `31` -> `32` (E = 32 overflows UInt64; the CPU fallback tests run E = 32)
        const int E = 31;
        using var runner = new FermatAlgorithm(E, 3);

        runner.RunBenchmarkChunk(17);
        var state = runner.GetState();

        Assert.Equal(2, state.Length);
        Assert.Equal(Squarings(E, 3, 17), FromWords(state));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task AsyncMembers_HostStage_MatchTheSynchronousOnes()
    {
        const int E = 29;
        using var runner = new FermatAlgorithm(E, 3);
        var reports = new List<FermatPrpProgress>();

        var expected = runner.Run();
        var result = await runner.RunAsync(new SynchronousProgress<FermatPrpProgress>(reports.Add));
        await runner.RunBenchmarkChunkAsync(4);
        var state = await runner.GetStateAsync();

        Assert.Equal(expected, result with { Elapsed = expected.Elapsed });
        Assert.Equal(runner.GetState(), state);
        Assert.NotEmpty(reports);
        Assert.Equal(result.Iterations, reports[^1].Iteration);
        Assert.Equal(reports[^1].TotalIterations, reports[^1].Iteration);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task AsyncMembers_PreCanceledToken_ThrowWithoutRunning()
    {
        using var runner = new FermatAlgorithm(13);
        var initial = runner.GetState();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunAsync(cancellationToken: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunBenchmarkChunkAsync(4, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.GetStateAsync(cts.Token));

        Assert.Equal(initial, runner.GetState());
        Assert.Equal(0, runner.Iteration);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FermatAlgorithm.MaxExponent + 1)]
    [InlineData(int.MaxValue)]
    public void Constructor_ExponentOutOfRange_ThrowsBeforeAllocating(int invalidE)
    {
        // Rejected before any workspace exists, so this needs no GPU; above 2^27 - 1 the square exceeds the NTT length.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(invalidE));
        Assert.Equal("e", exception.ParamName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1, 0, 3)]
    [InlineData(28, 0, 3)]
    [InlineData(0, -1, 3)]
    [InlineData(0, 0, 0)]
    public void Constructor_InvalidOptions_Throw(int blockSize, int checkIntervalBlocks, int maxConsecutiveRollbacks)
    {
        var options = new FermatPrpOptions
        {
            BlockSize = blockSize,
            CheckIntervalBlocks = checkIntervalBlocks,
            MaxConsecutiveRollbacks = maxConsecutiveRollbacks
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(13, 3, options));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Properties_DescribeTheTest()
    {
        using var runner = new FermatAlgorithm(31, 5);

        Assert.Equal(31, runner.E);
        Assert.Equal(5u, runner.Base);
        Assert.Equal(63, runner.ModulusBits);
        Assert.Equal(2, runner.ResidueWords);
        Assert.Equal(63, runner.ChainLength);
        Assert.Equal(7, runner.BlockSize); // clamp(floor(sqrt(63)), 2, 1000)
        Assert.Equal(ModulusOf(31), runner.GetModulus());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Disposed_ThrowsObjectDisposedExceptionFromEveryMember()
    {
        var runner = new FermatAlgorithm(5);
        var checkpoint = runner.CreateCheckpoint();
        runner.Dispose();
        runner.Dispose();

        // mutant: FermatAlgorithm.cs CreateCheckpoint `ThrowIfUnusable()` removed
        Assert.Throws<ObjectDisposedException>(() => runner.GetState());
        Assert.Throws<ObjectDisposedException>(() => runner.Run());
        Assert.Throws<ObjectDisposedException>(() => runner.RunBenchmarkChunk());
        Assert.Throws<ObjectDisposedException>(() => runner.CreateCheckpoint());
        Assert.Throws<ObjectDisposedException>(() => runner.Restore(checkpoint));
        Assert.Throws<ObjectDisposedException>(() => runner.GetModulus());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.RunAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.RunBenchmarkChunkAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.GetStateAsync());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void RunBenchmarkChunk_NegativeCount_Throws()
    {
        using var runner = new FermatAlgorithm(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunBenchmarkChunk(-1));
        Assert.Equal(0, runner.Iteration);
    }
}
