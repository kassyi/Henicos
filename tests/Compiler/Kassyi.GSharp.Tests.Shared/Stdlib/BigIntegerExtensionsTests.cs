using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Xunit;

namespace Kassyi.GSharp.Tests.Stdlib;

[Category(TestCategory.Unit)]
public class BigIntegerExtensionsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(10, 3)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(24, 4)]
    [InlineData(25, 5)]
    [InlineData(99, 9)]
    [InlineData(100, 10)]
    public void Sqrt_SmallValues_ReturnsExpectedFloorSqrt(long input, long expected)
    {
        var value = new BigInteger(input);
        var actual = value.Sqrt();
        Assert.Equal(new BigInteger(expected), actual);
    }

    [Fact]
    public void Sqrt_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        var negative = new BigInteger(-1);
        Assert.Throws<ArgumentOutOfRangeException>(() => negative.Sqrt());
    }

    [Fact]
    public void Sqrt_LargePerfectSquare_ReturnsExactRoot()
    {
        // 2^100 exact root is 2^50
        var expected = BigInteger.One << 50;
        var square = expected * expected;

        var actual = square.Sqrt();
        Assert.Equal(expected, actual);

        // square - 1 should yield expected - 1
        var justBelow = square - 1;
        Assert.Equal(expected - 1, justBelow.Sqrt());

        // square + 1 should still yield expected
        var justAbove = square + 1;
        Assert.Equal(expected, justAbove.Sqrt());
    }

    [Fact]
    public void Sqrt_ChudnovskyScaleValue_ComputesCorrectly()
    {
        // 10005 * 10^40
        var scale = 10005 * BigInteger.Pow(10, 40);
        var root = scale.Sqrt();

        // root^2 <= scale < (root + 1)^2
        Assert.True(root * root <= scale);
        Assert.True((root + 1) * (root + 1) > scale);
    }
}
