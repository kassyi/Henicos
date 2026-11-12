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

        // The integer part, the point and exactly targetDigits fraction digits, with nothing around them
        Assert.Equal("3.14159265358979323846264338327950288419716939937510", sw.ToString());
    }

    [SkippableFact]
    public void ComputePi_CalculatesCorrectHexDigits_WhenUsingStringWriter()
    {
        CudaCapability.EnsureGpuAvailable();

        // With asHex the digit count is the number of hex digits after the point: 80 of them (>= 50)
        var targetDigits = 80;
        using var sw = new StringWriter();

        ChudnovskyAlgorithm.ComputePi(targetDigits, sw, thresholdTerms: 8, asHex: true);

        var result = sw.ToString();
        // "3." and targetDigits uppercase hex digits, first 50: 243F6A8885A308D313198A2E03707344A4093822299F31D008
        Assert.Equal(targetDigits + 2, result.Length);
        Assert.StartsWith("3.243F6A8885A308D313198A2E03707344A4093822299F31D008", result);
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
