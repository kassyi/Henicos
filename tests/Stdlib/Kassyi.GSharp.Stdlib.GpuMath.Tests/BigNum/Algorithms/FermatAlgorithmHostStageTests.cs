namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Tests of <see cref="FermatAlgorithm"/>'s host stage (<c>E &lt;= 31</c>, <c>N &lt; 2^64</c>), which uses no GPU. The
/// expected state after <c>k</c> squarings of the base <c>a</c> is <c>a^(2^k) mod N</c> from <see cref="BigInteger"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmHostStageTests
{
    // E = 1, 3, 5, 11, 31 give a prime N; 7, 9, 13 a composite one; 2, 4, 8, 16 are even (3 divides N). E <= 3 is where
    // the GPU fold is undefined (c = 2^E - 11 <= 0, #532-1); E = 31 is the last host exponent (N has 63 bits).
    public static TheoryData<int, uint> HostExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 11, 13, 16, 31 })
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
    public void RunFermatPrp_HostStage_MatchesBigInteger(int e, uint @base)
    {
        using var runner = new FermatAlgorithm(e, @base);

        var (result, _) = runner.RunFermatPrp();

        // P-ref: E squarings give a^(2^E) mod N, in ceil((2E+1)/32) words.
        // mutant: FermatAlgorithm.cs HostResidueEngine.MultiplyMod `% _modulus` removed (the low 64 bits of the square)
        Assert.Equal((2 * e + 1 + 31) / 32, result.Length);
        Assert.Equal(Squarings(e, @base, e), FromWords(result));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RunFermatPrp_ExponentThree_IsTheValueTheIssueExpects()
    {
        // #532-1: N = 2^7 - 2^3 + 11 = 131 and 2^(2^3) mod 131 = 125; the old GPU fold returned another value.
        using var runner = new FermatAlgorithm(3);

        var (result, _) = runner.RunFermatPrp();

        Assert.Equal(new[] { 125u }, result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HostStage_LastExponent_HoldsTwoWordResidues()
    {
        // E = 31: N has 63 bits, so a residue needs both words and its square needs UInt128.
        // mutant: FermatAlgorithm.cs HostStageMaxExponent `31` -> `32` (E = 32 overflows UInt64; covered by the CPU fallback tests)
        const int E = 31;
        using var runner = new FermatAlgorithm(E, 3);

        runner.RunBenchmarkChunk(17);
        var state = runner.GetState();

        Assert.Equal(2, state.Length);
        Assert.Equal(Squarings(E, 3, 17), FromWords(state));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RunFermatPrpWithGerbiczCheck_HostStage_VerifiesAndMatchesBigInteger()
    {
        const int E = 30;
        using var runner = new FermatAlgorithm(E, 3);

        var (result, _, checksPassed, rollbacks) = runner.RunFermatPrpWithGerbiczCheck(blockSize: 5, checkStepBlocks: 2);

        // Checks at k = 10, 20 and 30 (E is a multiple of the block size), none fails.
        Assert.Equal(3, checksPassed);
        Assert.Equal(0, rollbacks);
        Assert.Equal(Squarings(E, 3, E), FromWords(result));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task AsyncMembers_HostStage_MatchTheSynchronousOnes()
    {
        const int E = 29;
        using var runner = new FermatAlgorithm(E, 3);

        var (result, _) = await runner.RunFermatPrpAsync();
        await runner.RunBenchmarkChunkAsync(4);
        var state = await runner.GetStateAsync();

        Assert.Equal(Squarings(E, 3, E), FromWords(result));
        Assert.Equal(Squarings(E, 3, E + 4), FromWords(state));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task AsyncMembers_PreCanceledToken_ThrowWithoutRunning()
    {
        using var runner = new FermatAlgorithm(13);
        var initial = runner.GetState();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunFermatPrpAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.RunBenchmarkChunkAsync(4, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runner.GetStateAsync(cts.Token));

        Assert.Equal(initial, runner.GetState());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void LoadState_HostStage_ChecksLengthAndRange()
    {
        const int E = 13;
        using var runner = new FermatAlgorithm(E);
        var n = ModulusOf(E);

        Assert.Throws<ArgumentException>(() => runner.LoadState(new uint[2]));
        Assert.Throws<ArgumentException>(() => runner.LoadState([(uint)n]));

        runner.LoadState([(uint)(n - 1)]);
        runner.RunBenchmarkChunk(1);

        // (N - 1)^2 = 1 (mod N).
        Assert.Equal(new[] { 1u }, runner.GetState());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Disposed_HostStage_ThrowsObjectDisposedException()
    {
        var runner = new FermatAlgorithm(5);
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.GetState());
        Assert.Throws<ObjectDisposedException>(() => runner.RunFermatPrp());
        Assert.Throws<ObjectDisposedException>(() => runner.RunBenchmarkChunk());
        Assert.Throws<ObjectDisposedException>(() => runner.LoadState([1u]));
    }
}
