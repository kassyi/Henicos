namespace Henicos.Benchmarks.Tests;

// The arguments of the gpu-pi command (Program.TryParseGpuPiArgs)
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuPiArgumentTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryParseGpuPiArgs_EveryOption_ReadsEachValue()
    {
        string[] args = ["500", "--out", "pi.txt", "--threshold", "64", "--hex"];

        var parsed = Program.TryParseGpuPiArgs(args, out var digits, out var outPath, out var threshold, out var asHex);

        Assert.True(parsed);
        Assert.Equal(500, digits);
        Assert.Equal("pi.txt", outPath);
        Assert.Equal(64, threshold);
        Assert.True(asHex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryParseGpuPiArgs_OutWithoutValue_IsIgnored()
    {
        string[] args = ["--out"];

        var parsed = Program.TryParseGpuPiArgs(args, out var digits, out var outPath, out var threshold, out var asHex);

        Assert.True(parsed);
        Assert.Equal(10000, digits);
        Assert.Null(outPath);
        Assert.Equal(8192, threshold);
        Assert.False(asHex);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryParseGpuPiArgs_ZeroThreshold_Fails()
    {
        string[] args = ["--threshold", "0"];

        Assert.False(Program.TryParseGpuPiArgs(args, out _, out _, out _, out _));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryParseGpuPiArgs_UpperCaseFlag_IsRecognized()
    {
        string[] args = ["--HEX"];

        Assert.True(Program.TryParseGpuPiArgs(args, out _, out _, out _, out var asHex));
        Assert.True(asHex);
    }

    // Only the first argument is a digit count; a number after a flag is ignored
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryParseGpuPiArgs_DigitCountAfterFlag_IsIgnored()
    {
        string[] args = ["--hex", "500"];

        Assert.True(Program.TryParseGpuPiArgs(args, out var digits, out _, out _, out var asHex));
        Assert.Equal(10000, digits);
        Assert.True(asHex);
    }
}
