using System.Linq;
using System.Numerics;

namespace Henicos.Benchmarks.Tests;

[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigIntModPowBenchmarkTests
{
    public static TheoryData<int, int> ValidBitWidths => new()
    {
        { 2048, 1 },
        { 2048, 31 },
        { 2048, 64 },
        { 8192, 1024 },
    };

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ParameterSources_ExposedValues_CoverExpectedModulusAndExponentMatrix()
    {
        var modulusBits = GpuBigIntModPowBenchmark.ModulusBitsValues().ToArray();
        var exponentBits = GpuBigIntModPowBenchmark.ExponentBitsValues().ToArray();

        Assert.Equal([2048, 4096, 8192, 16384, 32768], modulusBits);
        Assert.Equal([64, 1024], exponentBits);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ValidBitWidths))]
    public void CreateInputs_ValidBitWidths_ProducesExpectedWordLengths(int modulusBits, int exponentBits)
    {
        var (baseWords, expWords, modWords) = GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits);

        Assert.Equal((modulusBits / 32, modulusBits / 32, (exponentBits + 31) / 32),
            (modWords.Length, baseWords.Length, expWords.Length));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ValidBitWidths))]
    public void CreateInputs_ValidBitWidths_ProducesOddFullWidthModulus(int modulusBits, int exponentBits)
    {
        var (_, _, modWords) = GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits);

        Assert.Equal((1u, true), (modWords[0] & 1u, (modWords[^1] & 0x8000_0000u) != 0));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ValidBitWidths))]
    public void CreateInputs_ValidBitWidths_ProducesPositiveBaseLessThanModulus(int modulusBits, int exponentBits)
    {
        var (baseWords, _, modWords) = GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits);
        var baseVal = ToBigInteger(baseWords);

        Assert.InRange(baseVal, BigInteger.One, ToBigInteger(modWords) - 1);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ValidBitWidths))]
    public void CreateInputs_ValidBitWidths_ProducesExactExponentBitLength(int modulusBits, int exponentBits)
    {
        var (_, expWords, _) = GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits);

        Assert.Equal(exponentBits, ToBigInteger(expWords).GetBitLength());
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0, 64)]
    [InlineData(2049, 64)]
    [InlineData(2048, 0)]
    [InlineData(2048, -1)]
    public void CreateInputs_InvalidBitWidths_ThrowsArgumentOutOfRangeException(int modulusBits, int exponentBits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ModPow_BeforeSetup_ThrowsInvalidOperationException()
    {
        using var benchmark = new GpuBigIntModPowBenchmark();
        Assert.Throws<InvalidOperationException>(() => benchmark.ModPow());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_WithoutSetupAndTwice_IsIdempotent()
    {
        using var benchmark = new GpuBigIntModPowBenchmark();
        benchmark.Cleanup();
        benchmark.Dispose();
        Assert.Throws<InvalidOperationException>(() => benchmark.ModPow());
    }

    private static BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true);
    }
}
