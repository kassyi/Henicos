using System;
using Xunit;
using Henicos.Benchmarks.ComputeBenchmarks;

namespace Henicos.Benchmarks.Tests.ComputeBenchmarks;

public class BlackScholesKernelsTests
{
    [Fact]
    public void CalculateOptionPrice_ReturnsExpectedValues()
    {
        // Arrange
        float s = 100.0f; // Spot price
        float x = 100.0f; // Strike price
        float t = 1.0f;   // Time to maturity (1 year)
        float r = 0.05f;  // Risk-free rate (5%)
        float v = 0.2f;   // Volatility (20%)

        // Act
        BlackScholesKernels.CalculateOptionPrice(s, x, t, r, v, out float callResult, out float putResult);

        // Expected values calculated from standard Black-Scholes formula
        // d1 = (ln(1) + (0.05 + 0.02) * 1) / 0.2 = 0.07 / 0.2 = 0.35
        // d2 = 0.35 - 0.2 = 0.15
        // N(d1) ~ 0.63683, N(d2) ~ 0.55962
        // Call ~ 100 * 0.63683 - 100 * exp(-0.05) * 0.55962 ~ 10.4506
        // Put ~ 100 * exp(-0.05) * (1 - 0.55962) - 100 * (1 - 0.63683) ~ 5.574
        
        // Assert
        Assert.Equal(10.4505835f, callResult, precision: 4);
        Assert.Equal(5.573523f, putResult, precision: 4);
    }
}
