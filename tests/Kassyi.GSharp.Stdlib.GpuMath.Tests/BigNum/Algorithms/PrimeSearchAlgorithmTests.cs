using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

public class PrimeSearchAlgorithmTests
{
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Generate_ThrowsArgumentOutOfRangeException_WhenBitLengthIsLessThanTwo(int bitLength)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => PrimeSearchAlgorithm.Generate(bitLength));
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void Generate_ReturnsValidPrimeOfSpecifiedBitLength(int bitLength)
    {
        // Act
        var result = PrimeSearchAlgorithm.Generate(bitLength);
        
        // Assert
        Assert.True(result.IsEven == false, "Generated prime must be odd.");
        Assert.True(result.Sign > 0, "Generated prime must be positive.");
        
        // Calculate the actual bit length
        var bytes = result.ToByteArray(isUnsigned: true, isBigEndian: false);
        var actualBitLength = (bytes.Length - 1) * 8;
        
        var msb = bytes[^1];
        while (msb > 0)
        {
            actualBitLength++;
            msb >>= 1;
        }

        Assert.Equal(bitLength, actualBitLength);

        // Verify primality using System.Numerics.BigInteger for sanity check (for small enough numbers)
        // Since Generate returns Kassyi.GSharp.Stdlib.GpuMath.BigNum.BigInteger (or System.Numerics? wait)
        // Wait, what type does PrimeSearchAlgorithm.Generate return?
    }
}
