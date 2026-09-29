using Kassyi.GSharp.Runtime.Interop;
using Xunit;

namespace Henicos.Benchmarks.Tests;

[Trait("Category", "GpuExecution")]
public class GpuChudnovskyPiTests
{
    [Fact]
    public void ComputePi_CalculatesCorrectDecimalDigits_WhenUsingStringWriter()
    {
        // Arrange
        // Verify a small number of digits (e.g., 50) to keep the test fast
        var targetDigits = 50;
        using var sw = new StringWriter();
        
        // Act
        // This will initialize the CudaContextScope internally and use the StringWriter
        GpuChudnovskyPi.ComputePi(targetDigits, sw, thresholdTerms: 8, asHex: false);

        // Assert
        var result = sw.ToString().Trim();
        
        
        Assert.False(string.IsNullOrWhiteSpace(result));
        
        // Remove any possible formatting/decimal points for a clean assert
        var cleanResult = result.Replace(".", "");
        
        Assert.StartsWith("314159265358979323846264338327950288419716939937510", cleanResult);
    }

    [Fact]
    public void ComputePi_CalculatesCorrectHexDigits_WhenUsingStringWriter()
    {
        // Arrange
        var targetDigits = 50; 
        using var sw = new StringWriter();
        
        // Act
        GpuChudnovskyPi.ComputePi(targetDigits, sw, thresholdTerms: 8, asHex: true);

        // Assert
        var result = sw.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(result));
        
        // Hex Pi starts with 3.243F6A8885A308D3
        var cleanResult = result.Replace(".", "");
        Assert.StartsWith("3243F6A8885A308D3", cleanResult.ToUpperInvariant());
    }
}
