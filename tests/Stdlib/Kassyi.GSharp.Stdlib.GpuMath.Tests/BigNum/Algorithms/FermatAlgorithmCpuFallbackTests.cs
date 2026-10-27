namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

using static FermatTestMath;

/// <summary>
/// Tests of <see cref="FermatAlgorithm"/>'s device stage under the CPU fallback (#647) and of the fallback mode it keeps
/// from construction. The expected state after <c>k</c> squarings of the base <c>a</c> is <c>a^(2^k) mod N</c> from
/// <see cref="BigInteger"/>.
/// </summary>
/// <remarks><see cref="GpuKernelLoader.ForceCpuFallback"/> is AsyncLocal, so each test sets and restores it itself.</remarks>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class FermatAlgorithmCpuFallbackTests
{
    // E = 32 is the first device exponent (the host stage ends at 31); 257 / 258 / 300 squarings are more than one
    // 256-squaring graph of the GPU path, which the CPU fallback replaced with a premature return before #647.
    public static TheoryData<int, uint> FallbackExponents
    {
        get
        {
            var data = new TheoryData<int, uint>();
            foreach (var e in new[] { 32, 33, 51, 257, 258, 300 })
            {
                data.Add(e, 2u);
                data.Add(e, 3u);
            }

            return data;
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(FallbackExponents))]
    public void RunFermatPrp_CpuFallback_MatchesBigIntegerAndTheGpu(int e, uint @base)
    {
        uint[] cpuResult;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(e, @base);
            (cpuResult, _) = runner.RunFermatPrp();
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }

        // P-ref: every one of the E squarings runs on the host (Mul by BigInteger, FoldReference).
        // mutant: FermatAlgorithm.cs DeviceResidueEngine.SquareState `if (_isCpu)` -> `if (_isCpu && count < 0)` (graph path: EndCapture gives no graph)
        Assert.Equal((2 * e + 1 + 31) / 32, cpuResult.Length);
        Assert.Equal(Squarings(e, @base, e), FromWords(cpuResult));

        // P-agree: the GPU returns the same words, when there is a GPU.
        if (!GpuKernelLoader.IsDriverAvailable)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        using var gpuRunner = new FermatAlgorithm(e, @base);
        var (gpuResult, _) = gpuRunner.RunFermatPrp();
        Assert.Equal(gpuResult, cpuResult);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetState_CpuFallback_FoldedSquareAboveN_IsReturnedCanonical()
    {
        const int E = 40;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E);

            // FoldReference turns (N - 1)^2 into N + 1; the read subtracts N.
            runner.LoadState(ToWords(ModulusOf(E) - 1, runner.OutNumWords));
            runner.RunBenchmarkChunk(1);

            var expected = new uint[runner.OutNumWords];
            expected[0] = 1;
            Assert.Equal(expected, runner.GetState());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RunFermatPrpWithGerbiczCheck_CpuFallback_VerifiesAndMatchesBigInteger()
    {
        const int E = 40;
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var runner = new FermatAlgorithm(E, 3);

            var (result, _, checksPassed, rollbacks) = runner.RunFermatPrpWithGerbiczCheck(blockSize: 10, checkStepBlocks: 2);

            // Checks at k = 20 and 40; the products of the check go through the CPU provider and FoldReference.
            Assert.Equal(2, checksPassed);
            Assert.Equal(0, rollbacks);
            Assert.Equal(Squarings(E, 3, E), FromWords(result));
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
        const int E = 40;
        using var scope = new GpuKernelLoader.CudaContextScope();
        var runner = new FermatAlgorithm(E, 3);
        try
        {
            GpuKernelLoader.ForceCpuFallback = true;
            try
            {
                // Device pointers would reach host code (and host pointers CUDA kernels, #647); every member refuses,
                // Dispose included, which then releases nothing.
                        Assert.Throws<InvalidOperationException>(() => runner.GetState());
                Assert.Throws<InvalidOperationException>(() => runner.RunBenchmarkChunk(1));
                Assert.Throws<InvalidOperationException>(() => runner.RunFermatPrp());
                Assert.Throws<InvalidOperationException>(() => runner.LoadState(new uint[runner.OutNumWords]));
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
        const int E = 40;
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
