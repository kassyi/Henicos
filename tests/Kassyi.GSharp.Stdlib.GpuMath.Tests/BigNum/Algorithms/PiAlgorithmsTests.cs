using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Xunit;

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
    }

    [Theory]
    [InlineData(10, "31415926535")] // 10 digits after 3, making length 11 (3.1415926535)
    [InlineData(50, "314159265358979323846264338327950288419716939937510")]
    public void Test_ChudnovskyAlgorithm_ComputesPiCorrectly(int digits, string expectedDecimal)
    {
        var piString = ChudnovskyAlgorithm.ComputePi(digits);
        Assert.Equal(expectedDecimal, piString);
    }
}
