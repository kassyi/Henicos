
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection("GpuWorkspace")]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FermatAlgorithmTests
{
    [SkippableTheory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void FermatAlgorithm_RunFermatPrp_MatchesCpuBigInteger_SmallE(int e)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e);

        var (resultArray, elapsedMs) = runner.RunFermatPrp();
        Assert.True(elapsedMs >= 0);

        var actual = GetBigIntegerFromResult(resultArray, e);

        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger expected = Enumerable.Range(0, e)
            .Aggregate(new BigInteger(2), (cur, _) => (cur * cur) % n);

        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void FermatAlgorithm_RunFermatPrpWithGerbiczCheck_VerifiesAndMatchesCpuBigInteger()
    {
        CudaCapability.EnsureGpuAvailable();
        const int E = 40;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(E);

        var (resultArray, _, checksPassed, _) = runner.RunFermatPrpWithGerbiczCheck(blockSize: 10, checkStepBlocks: 2);
        Assert.True(checksPassed >= 2);

        var actual = GetBigIntegerFromResult(resultArray, E);
        var x = BigInteger.One << E;
        var n = (BigInteger.One << (2 * E + 1)) - x + 11;
        BigInteger expected = Enumerable.Range(0, E)
            .Aggregate(new BigInteger(2), (cur, _) => (cur * cur) % n);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void FermatAlgorithm_Constructor_NonPositiveExponent_ThrowsArgumentOutOfRangeException(int invalidE)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FermatAlgorithm(invalidE));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void FermatAlgorithm_LoadState_And_Disposed_ThrowsExpectedExceptions()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var runner = new FermatAlgorithm(4);

        Assert.Throws<ArgumentNullException>(() => runner.LoadState(null!));
        Assert.Throws<ArgumentException>(() => runner.LoadState(new uint[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunFermatPrpWithGerbiczCheck(blockSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunFermatPrpWithGerbiczCheck(checkStepBlocks: 0));

        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.GetState());
        Assert.Throws<ObjectDisposedException>(() => runner.RunFermatPrp());
        Assert.Throws<ObjectDisposedException>(() => runner.RunBenchmarkChunk());
    }



    [Fact(Skip =
        "cuMemGetInfo_v2 measures global device VRAM and is extremely flaky on shared GPUs. Use Benchmark CLI to measure leaks instead.")]
    public void FermatAlgorithm_VramLeak_Test()
    {
        int e = 12;
        using var scope = new GpuKernelLoader.CudaContextScope();

        // Force JIT compilation overhead before tracking memory
        using (var warmupRunner = new FermatAlgorithm(5))
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

    private static BigInteger GetBigIntegerFromResult(uint[] resultArray, int e)
    {
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        var highestWord = resultArray[^1];
        var resBytes = new byte[resultArray.Length * 4 + 1];
        Buffer.BlockCopy(resultArray, 0, resBytes, 0, resultArray.Length * 4);
        var isNegative = (highestWord & 0x80000000) != 0;
        resBytes[resultArray.Length * 4] = isNegative ? (byte)0xFF : (byte)0x00;

        var actual = new BigInteger(resBytes);
        actual %= n;
        if (actual < 0)
        {
            actual += n;
        }

        return actual;
    }
}
