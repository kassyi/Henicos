using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Benchmarks.Tests;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuChudnovskyPiTests
{
    [SkippableFact]
    public void ComputePi_CalculatesCorrectDecimalDigits_WhenUsingStringWriter()
    {
        CudaCapability.EnsureGpuAvailable();

        var targetDigits = 50;
        using var sw = new StringWriter();

        ChudnovskyAlgorithm.ComputePi(targetDigits, sw, thresholdTerms: 8, asHex: false);

        var result = sw.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(result));

        var cleanResult = result.Replace(".", "");
        Assert.Equal(targetDigits + 1, cleanResult.Length);
        Assert.Equal("314159265358979323846264338327950288419716939937510", cleanResult);
    }

    [SkippableFact]
    public void ComputePi_CalculatesCorrectHexDigits_WhenUsingStringWriter()
    {
        CudaCapability.EnsureGpuAvailable();

        // 60 decimal digits => floor(60 * 3.321928 / 32) * 32 = 192 bits = 48 hex digits (>= 40)
        // Or 80 decimal digits => floor(80 * 3.321928 / 32) * 32 = 256 bits = 64 hex digits (>= 50)
        var targetDigits = 80;
        using var sw = new StringWriter();

        ChudnovskyAlgorithm.ComputePi(targetDigits, sw, thresholdTerms: 8, asHex: true);

        var result = sw.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(result));

        var cleanResult = result.Replace(".", "").ToUpperInvariant();
        // 3. followed by 50 hex digits of Pi: 243F6A8885A308D313198A2E03707344A4093822299F31D008
        Assert.True(cleanResult.Length >= 51, $"Expected at least 51 hex characters, got {cleanResult.Length}");
        Assert.StartsWith("3243F6A8885A308D313198A2E03707344A4093822299F31D008", cleanResult);
    }

    [Fact]
    public void ComputePi_InvalidArguments_ThrowExpectedExceptions()
    {
        using var sw = new StringWriter();

        // [WHY] Intentionally pass null TextWriter to verify ArgumentNullException guard.
        Assert.Throws<ArgumentNullException>(() => ChudnovskyAlgorithm.ComputePi(10, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(0, sw));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(-10, sw));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(10, sw, thresholdTerms: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(10, sw, thresholdTerms: -1));
    }
}
