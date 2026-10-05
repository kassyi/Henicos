using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuFixedUIntTests
{
    [Fact]
    public void GpuUInt128_ZeroAndOne_EqualityAndComparison()
    {
        GpuUInt128 zero = 0u;
        GpuUInt128 one = 1u;
        GpuUInt128 zeroAlt = [0u, 0u, 0u, 0u];
        GpuUInt128 oneAlt = [1u, 0u, 0u, 0u];

        Assert.Equal(zero, zeroAlt);
        Assert.Equal(one, oneAlt);
        Assert.True(zero == zeroAlt);
        Assert.True(zero != one);
        Assert.True(zero < one);
        Assert.True(zero <= one);
        Assert.True(one > zero);
        Assert.True(one >= zero);
        Assert.True(zero <= zeroAlt);
        Assert.True(zero >= zeroAlt);
        Assert.Equal(0, zero.CompareTo(zeroAlt));
        Assert.Equal(-1, zero.CompareTo(one));
        Assert.Equal(1, one.CompareTo(zero));
    }

    [Fact]
    public void GpuUInt128_BoundaryValues_MaxAndMultiLimb()
    {
        GpuUInt128 max = [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu];
        GpuUInt128 almostMax = [0xFFFFFFFEu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu];
        GpuUInt128 highLimbOnly = [0u, 0u, 0u, 1u];

        Assert.True(almostMax < max);
        Assert.True(max > almostMax);
        Assert.True(highLimbOnly > (GpuUInt128)0xFFFFFFFFu);
        Assert.NotEqual(max.GetHashCode(), almostMax.GetHashCode());
    }

    [Fact]
    public void GpuUInt256_ZeroAndOne_EqualityAndComparison()
    {
        GpuUInt256 zero = 0u;
        GpuUInt256 one = 1u;
        GpuUInt256 zeroAlt = [0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];
        GpuUInt256 oneAlt = [1u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];

        Assert.Equal(zero, zeroAlt);
        Assert.Equal(one, oneAlt);
        Assert.True(zero == zeroAlt);
        Assert.True(zero != one);
        Assert.True(zero < one);
        Assert.True(one > zero);
    }

    [Fact]
    public void GpuUInt256_BoundaryValues_MaxAndMultiLimb()
    {
        GpuUInt256 max = [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu];
        GpuUInt256 almostMax = [0xFFFFFFFEu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu];

        Assert.True(almostMax < max);
        Assert.True(max > almostMax);
    }

    [Fact]
    public void GpuUInt512_ZeroAndOne_EqualityAndComparison()
    {
        GpuUInt512 zero = 0u;
        GpuUInt512 one = 1u;
        GpuUInt512 zeroAlt = [0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];
        GpuUInt512 oneAlt = [1u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];

        Assert.Equal(zero, zeroAlt);
        Assert.Equal(one, oneAlt);
        Assert.True(zero == zeroAlt);
        Assert.True(zero < one);
    }
}
