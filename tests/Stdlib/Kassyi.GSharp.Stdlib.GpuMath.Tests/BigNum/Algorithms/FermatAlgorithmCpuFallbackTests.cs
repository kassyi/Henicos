namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Tests of <see cref="FermatAlgorithm"/>'s device stage under the CPU fallback (#647) and of the fallback mode it keeps
/// from construction. References come from <see cref="BigInteger"/>.
/// </summary>
/// <remarks><see cref="GpuKernelLoader.ForceCpuFallback"/> is AsyncLocal, so each test sets and restores it itself.</remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmCpuFallbackTests
{
    // E = 32 is the first device exponent (the host stage ends at 31); 257 / 258 / 300 run chains longer than one
    // 256-squaring graph of the GPU path, which the CPU fallback replaced with a premature return before #647. Base 3
    // only for odd E (for even E, 3 divides N and no chain runs).
    public static TheoryData<int, uint> FallbackExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 32, 33, 51, 257, 258, 300 })
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

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(FallbackExponents))]
    public void Run_CpuFallback_MatchesBigIntegerAndTheGpu(int e, uint @base)
    {
        FermatPrpResult cpuResult;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(e, @base);
            cpuResult = runner.Run();
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        // P-ref and P-verdict: every squaring of the chain and of the checks runs on the host (BigInteger, FoldReference).
        // mutant: QuadraticDeviceModulus.cs SquareState `if (_isCpu)` -> `if (_isCpu && count < 0)` (graph path: EndCapture gives no graph)
        var expected = PrpReference(e, @base);
        Assert.Equal(expected.IsProbablePrime, cpuResult.IsProbablePrime);
        Assert.Equal(expected.Residue64, cpuResult.Residue64);
        Assert.Equal(expected.CheckResidue64, cpuResult.CheckResidue64);
        Assert.Equal(0, cpuResult.Rollbacks);

        // P-agree: the GPU gives the same result, when there is a GPU.
        if (!GpuKernelLoader.IsDriverAvailable)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var gpuRunner = new FermatAlgorithm(e, @base);
        var gpuResult = gpuRunner.Run();
        Assert.Equal(gpuResult, cpuResult with { Elapsed = gpuResult.Elapsed });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SmallDivisor_CpuFallback_AllocatesNoRegistersOrScratch()
    {
        // E = 40, base 3: the device stage would allocate the registers, the product and the workspace; with the
        // divisor 3 known, Run needs none of them (PR #761 review 1), and Dispose has nothing to release.
        const int E = 40;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E);

            var result = runner.Run();

            Assert.Equal(3u, result.SmallDivisor);
            Assert.False(runner.HasChain);
            Assert.Equal(runner.ResidueWords, runner.CreateCheckpoint().State.Length);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetState_CpuFallback_FoldedSquareAboveN_IsReturnedCanonical()
    {
        const int E = 40;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E, 2);

            // FoldReference turns (N - 1)^2 into N + 1; the read subtracts N.
            runner.Restore(FermatAlgorithmTests.With(runner.CreateCheckpoint(),
                state: ToWords(ModulusOf(E) - 1, runner.ResidueWords)));
            runner.RunBenchmarkChunk(1);

            var expected = new uint[runner.ResidueWords];
            expected[0] = 1;
            Assert.Equal(expected, runner.GetState());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Restore_LastPoint_CpuFallback_ResumesToTheSameResult()
    {
        const int E = 61;
        var options = new FermatPrpOptions { BlockSize = 7 };
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            var checkpoints = new List<FermatPrpCheckpoint>();
            FermatPrpResult expected;
            using (var source = new FermatAlgorithm(E, 3, options with { OnVerifiedCheckpoint = checkpoints.Add }))
            {
                expected = source.Run();
            }

            // The last point (k = 126 > K = 123) carries u_K; resuming from it squares nothing and decides again.
            using var resumed = new FermatAlgorithm(E, 3, options);
            resumed.Restore(checkpoints[^1]);
            var result = resumed.Run();

            Assert.True(expected.IsProbablePrime);
            Assert.True(result.IsProbablePrime);
            Assert.Equal(expected.Residue64, result.Residue64);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Run_CpuFallbackWithFrequentChecks_VerifiesAndMatchesBigInteger()
    {
        const int E = 41;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E, 3, new FermatPrpOptions { BlockSize = 10, CheckIntervalBlocks = 2 });

            var result = runner.Run();

            // K' = 90: checks at 20, 40, 60, 80 and 90; the products of the checks go through the CPU provider.
            Assert.Equal(5, result.ChecksPassed);
            Assert.Equal(0, result.Rollbacks);
            Assert.Equal(PrpReference(E, 3).Residue64, result.Residue64);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void GpuInstance_AfterSwitchingToTheCpuFallback_ThrowsBeforeTouchingItsBuffers()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 41;
        using var scope = new GpuKernelLoader.CudaContextScope();
        var runner = new FermatAlgorithm(E, 3);
        try
        {
            var checkpoint = runner.CreateCheckpoint();
            GpuKernelLoader.ForceCpuFallback = true;
            try
            {
                // Device pointers would reach host code (and host pointers CUDA kernels, #647); every member refuses,
                // Dispose included, which then releases nothing.
                Assert.Throws<InvalidOperationException>(() => runner.GetState());
                Assert.Throws<InvalidOperationException>(() => runner.RunBenchmarkChunk(1));
                Assert.Throws<InvalidOperationException>(() => runner.Run());
                Assert.Throws<InvalidOperationException>(() => runner.Restore(checkpoint));
                Assert.Throws<InvalidOperationException>(() => runner.CreateCheckpoint());
                Assert.Throws<InvalidOperationException>(() => runner.Dispose());
            }
            finally
            {
                GpuKernelLoader.ForceCpuFallback = false;
            }

            // Nothing ran and nothing was released: the instance still works in its own mode.
            Assert.Equal(new BigInteger(3), FromWords(runner.GetState()));
            runner.RunBenchmarkChunk(2);
            Assert.Equal(Squarings(E, 3, 2), FromWords(runner.GetState()));
        }
        finally
        {
            runner.Dispose();
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void CpuInstance_AfterLeavingTheCpuFallback_Throws()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 41;
        GpuKernelLoader.ForceCpuFallback = true;
        var runner = new FermatAlgorithm(E, 3);
        try
        {
            GpuKernelLoader.ForceCpuFallback = false;

            // Host buffers would reach the CUDA kernels.
            Assert.Throws<InvalidOperationException>(() => runner.RunBenchmarkChunk(1));
            Assert.Throws<InvalidOperationException>(() => runner.GetState());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = true;
            runner.Dispose();
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HostStage_IgnoresTheFallbackMode()
    {
        // E <= 31 never touches a device or CPU-fallback buffer, so switching the mode is harmless there.
        const int E = 31;
        GpuKernelLoader.ForceCpuFallback = true;
        FermatAlgorithm runner;
        try
        {
            runner = new FermatAlgorithm(E, 3);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        using (runner)
        {
            runner.RunBenchmarkChunk(3);
            Assert.Equal(Squarings(E, 3, 3), FromWords(runner.GetState()));
        }
    }
}
