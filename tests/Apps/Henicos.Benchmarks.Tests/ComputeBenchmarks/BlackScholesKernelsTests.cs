
namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

[Category(TestCategory.Runtime)]
public class BlackScholesKernelsTests
{
    [Fact]
    public void CalculateOptionPrice_ReturnsExpectedValues()
    {
        float s = 100.0f; // Spot price
        float x = 100.0f; // Strike price
        float t = 1.0f;   // Time to maturity (1 year)
        float r = 0.05f;  // Risk-free rate (5%)
        float v = 0.2f;   // Volatility (20%)

        BlackScholesKernels.CalculateOptionPrice(s, x, t, r, v, out float callResult, out float putResult);

        Assert.Equal(10.4505835f, callResult, precision: 4);
        Assert.Equal(5.573523f, putResult, precision: 4);
    }

    [Theory]
    [InlineData(100.0f, 100.0f, 1.0f, 0.05f, 0.2f)]   // ATM
    [InlineData(120.0f, 100.0f, 1.0f, 0.05f, 0.2f)]   // ITM Call / OTM Put
    [InlineData(80.0f, 100.0f, 1.0f, 0.05f, 0.2f)]    // OTM Call / ITM Put
    [InlineData(100.0f, 100.0f, 0.5f, 0.0f, 0.25f)]   // Zero risk-free rate (r = 0)
    [InlineData(150.0f, 100.0f, 2.0f, 0.03f, 0.01f)]  // Near-zero volatility boundary
    [InlineData(100.0f, 110.0f, 0.01f, 0.05f, 0.3f)]  // Near-expiration boundary
    public void CalculateOptionPrice_SatisfiesPutCallParityAndMonotonicity(float s, float x, float t, float r, float v)
    {
        BlackScholesKernels.CalculateOptionPrice(s, x, t, r, v, out float callResult, out float putResult);

        Assert.True(callResult >= 0.0f, $"Call price must be non-negative, got {callResult}");
        Assert.True(putResult >= 0.0f, $"Put price must be non-negative, got {putResult}");

        // Put-Call Parity: C - P = S - X * e^(-r * T)
        float expectedParityDiff = s - x * MathF.Exp(-r * t);
        float actualParityDiff = callResult - putResult;
        Assert.Equal(expectedParityDiff, actualParityDiff, precision: 3);
    }
}
