using System.Linq;
using System.Numerics;

namespace Henicos.Benchmarks.Tests;

[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigIntModPowBenchmarkTests
{
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
    [InlineData(2048, 1)]
    [InlineData(2048, 31)]
    [InlineData(2048, 64)]
    [InlineData(8192, 1024)]
    public void CreateInputs_ValidBitWidths_SatisfiesModulusBaseAndExponentInvariants(
        int modulusBits,
        int exponentBits)
    {
        var (baseWords, expWords, modWords) = GpuBigIntModPowBenchmark.CreateInputs(modulusBits, exponentBits);

        Assert.Equal(modulusBits / 32, modWords.Length);
        Assert.Equal(modulusBits / 32, baseWords.Length);
        Assert.Equal((exponentBits + 31) / 32, expWords.Length);

        // Modulus has both LSB (odd) and MSB set.
        Assert.Equal(1u, modWords[0] & 1u);
        Assert.True((modWords[^1] & 0x8000_0000u) != 0);

        // Base is non-zero and strictly less than modulus.
        var baseVal = ToBigInteger(baseWords);
        var expVal = ToBigInteger(expWords);
        var modVal = ToBigInteger(modWords);
        Assert.True(baseVal > BigInteger.Zero);
        Assert.True(baseVal < modVal);

        // Exponent has exact bit-length ExponentBits.
        Assert.Equal(exponentBits, expVal.GetBitLength());
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
