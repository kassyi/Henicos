using System;
using System.Numerics;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Collection("GpuWorkspace")]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FermatAlgorithmTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FermatAlgorithm_RunFermatPrp_MatchesCpuBigInteger_SmallE(int e)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e);

        var (resultArray, _) = runner.RunFermatPrp();

        var actual = GetBigIntegerFromResult(resultArray, e);

        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger expected = 2;
        for (int i = 0; i < e; i++)
        {
            expected = (expected * expected) % n;
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FermatAlgorithm_RunFermatPrp_MatchesCpuBigInteger_E10()
    {
        int e = 10;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var runner = new FermatAlgorithm(e);

        var (resultArray, _) = runner.RunFermatPrp();

        // Convert resultArray to BigInteger
        var actual = GetBigIntegerFromResult(resultArray, e);

        // Compute correct expected value on CPU
        var x = BigInteger.One << e;
        var n = (BigInteger.One << (2 * e + 1)) - x + 11;
        BigInteger expected = 2;
        for (int i = 0; i < e; i++)
        {
            expected = (expected * expected) % n;
        }

        Assert.Equal(expected, actual);
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
