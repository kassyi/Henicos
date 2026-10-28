#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Shows that <see cref="FermatAlgorithm"/>'s Gerbicz-Li check detects errors and recovers from them, by corrupting the
/// state through the internal hook <c>CorruptStateAt</c>. The "returns the right value" tests cannot show this: a check
/// that always passes returns the right value too when nothing goes wrong.
/// </summary>
/// <remarks>
/// E = 31 runs on the host and needs no GPU; E = 41 runs on the GPU. With B = 5 and a check every 2 blocks the chain of
/// K = 2E + 1 squarings is checked at 10, 20, ... and at K' = roundUp(K, 5).
/// </remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmFaultInjectionTests
{
    private static readonly FermatPrpOptions s_options = new() { BlockSize = 5, CheckIntervalBlocks = 2 };

    public static TheoryData<int> Exponents => [31, 41];

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(Exponents))]
    public void Run_OneErrorInsideABlock_RollsBackOnceAndGivesTheRightResult(int e)
    {
        using var context = Enter(e);
        using var runner = new FermatAlgorithm(e, 3, s_options);
        runner.CorruptStateAt = OnceAt(17);

        var result = runner.Run();

        // G2: the error at k = 17 fails the check at 20, the chain goes back to the point verified at 10 and squares
        // those 10 again; the result is still the reference.
        // mutant: SquaringChainDriver.cs RunAsync `d.AsSpan().SequenceEqual(aux)` -> `d.AsSpan().SequenceEqual(d)` (the check always passes)
        // mutant: SquaringChainDriver.cs RunAsync the call of the fault hook removed
        var endIteration = EndIteration(e);
        Assert.Equal(1, result.Rollbacks);
        Assert.Equal(ExpectedChecks(endIteration), result.ChecksPassed);
        Assert.Equal(endIteration + 10, result.Iterations);
        AssertReference(e, result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Exponents))]
    public void Run_ErrorRightAfterAPassedCheck_RollsBackToThatCheck(int e)
    {
        using var context = Enter(e);
        using var runner = new FermatAlgorithm(e, 3, s_options);
        runner.CorruptStateAt = OnceAt(21);

        var result = runner.Run();

        // The point verified at 20 already holds d = u_0 * u_5 * ... * u_20. After the rollback to it, the chain goes on
        // with the next block and must not multiply u_20 into d a second time; otherwise every later check fails.
        // mutant: SquaringChainDriver.cs RunAsync rollback: `Load(verified);` followed by `Multiply(CheckD, CheckD, State)` (d extended twice)
        // mutant: SquaringChainDriver.cs RunAsync rollback: `Load(verified);` removed (the corrupted state goes on)
        var endIteration = EndIteration(e);
        Assert.Equal(1, result.Rollbacks);
        Assert.Equal(ExpectedChecks(endIteration), result.ChecksPassed);
        Assert.Equal(endIteration + 10, result.Iterations);
        AssertReference(e, result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Exponents))]
    public void Run_TwoSeparateErrors_CountTheFailuresInARowFromTheLastPass(int e)
    {
        using var context = Enter(e);

        // At most 2 failures in a row; the two errors are separated by passed checks, so neither ends the run.
        using var runner = new FermatAlgorithm(e, 3, s_options with { MaxConsecutiveRollbacks = 2 });
        runner.CorruptStateAt = OnceAt(17, 47);

        var result = runner.Run();

        // mutant: SquaringChainDriver.cs RunAsync `consecutiveFailures = 0;` after a passed check removed
        Assert.Equal(2, result.Rollbacks);
        Assert.Equal(EndIteration(e) + 20, result.Iterations);
        AssertReference(e, result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(Exponents))]
    public void Run_ErrorEveryTime_ThrowsAtTheThirdFailureInARow(int e)
    {
        using var context = Enter(e);
        using var runner = new FermatAlgorithm(e, 3, s_options);
        var calls = 0;
        runner.CorruptStateAt = _ =>
        {
            calls++;
            return true;
        };

        // G3: every block is corrupted, so the check at 10 fails, the chain goes back to k = 0 and fails again; the
        // third failure in a row ends the run.
        // mutant: SquaringChainDriver.cs RunAsync `consecutiveFailures >= MaxConsecutiveFailures` -> `>`
        var exception = Assert.Throws<InvalidOperationException>(() => runner.Run());
        Assert.Contains("3 times in a row", exception.Message);
        Assert.Contains("at iteration 10", exception.Message);
        Assert.Equal(30, calls);

        // The checkpoint is still the start of the chain: nothing unverified is handed out.
        Assert.Equal(0, runner.CreateCheckpoint().Iteration);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_OneErrorUnderTheCpuFallback_RollsBackOnce()
    {
        const int E = 41;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E, 3, s_options);
            runner.CorruptStateAt = OnceAt(17);

            var result = runner.Run();

            Assert.Equal(1, result.Rollbacks);
            AssertReference(E, result);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    // The device stage needs a GPU and a context; the host stage needs neither.
    private static IDisposable? Enter(int e)
    {
        if (e <= 31)
        {
            return null;
        }

        CudaCapability.EnsureGpuAvailable();
        return new GpuKernelLoader.CudaContextScope();
    }

    // Corrupts the state once at each of the given positions (not again when a rollback passes them a second time).
    private static Func<long, bool> OnceAt(params long[] positions)
    {
        var pending = new HashSet<long>(positions);
        return pending.Remove;
    }

    private static long EndIteration(int e) => (2L * e + 1 + 4) / 5 * 5;

    // A check at every multiple of 10 and one at K' when K' is not a multiple of 10.
    private static int ExpectedChecks(long endIteration) => (int)(endIteration / 10 + (endIteration % 10 != 0 ? 1 : 0));

    private static void AssertReference(int e, FermatPrpResult result)
    {
        var expected = PrpReference(e, 3);
        Assert.Equal(expected.Residue64, result.Residue64);
        Assert.Equal(expected.CheckResidue64, result.CheckResidue64);
        Assert.Equal(expected.IsProbablePrime, result.IsProbablePrime);
    }
}
