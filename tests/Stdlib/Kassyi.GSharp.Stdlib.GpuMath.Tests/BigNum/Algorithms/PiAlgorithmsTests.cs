using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.Diagnostics;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

[TestTarget(ExecutionTarget.CpuExecution)]
public class PiAlgorithmsTests
{
    [Fact]
    public void Test_BbpAlgorithm_ExtractsHexDigitsCorrectly()
    {
        // Pi in hex: 3.243F6A8885A308D313198A2E03707344A4093822299F31D0082EFA98EC4E6C89...
        // Offset 0 starts from the first fractional digit '2'
        
        var first10 = BbpAlgorithm.ExtractHexDigits(0, 10);
        Assert.Equal("243F6A8885", first10.ToUpperInvariant());
        
        var next10 = BbpAlgorithm.ExtractHexDigits(10, 10);
        Assert.Equal("A308D31319", next10.ToUpperInvariant());

        var largeOff = BbpAlgorithm.ExtractHexDigits(10000, 2);
        Assert.Equal(2, largeOff.Length);
    }

    [Theory]
    [InlineData(10, "31415926535")] // 10 digits after 3, making length 11 (3.1415926535)
    [InlineData(50, "314159265358979323846264338327950288419716939937510")]
    public void Test_ChudnovskyAlgorithm_Cpu_ComputesPiCorrectly(int digits, string expectedDecimal)
    {
        var piString = ChudnovskyAlgorithm.ComputePi(digits);
        Assert.Equal(expectedDecimal, piString);
    }

    [SkippableTheory]
    [InlineData(10, false, "3.1415926535")]
    [InlineData(10, true, "3.243F6A88")]
    public void Test_ChudnovskyAlgorithm_Gpu_ComputesPiCorrectly(int digits, bool asHex, string expected)
    {
        CudaCapability.EnsureGpuAvailable();
        using var sw = new StringWriter();
        ChudnovskyAlgorithm.ComputePi(digits, sw, 1, asHex);
        var output = sw.ToString();
        Assert.Contains(expected.ToUpperInvariant(), output.ToUpperInvariant());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PiVerifier_VerifyWithBbp_ValidatesCorrectAndIncorrectHexTails()
    {
        Assert.True(PiVerifier.VerifyWithBbp("243F6A88", 0));
        Assert.False(PiVerifier.VerifyWithBbp("DEADBEEF", 0));
        Assert.False(PiVerifier.VerifyWithBbp(string.Empty, 0));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ChudnovskyAlgorithm_Cpu_InvalidArguments_ThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(4, 2));
    }
}
