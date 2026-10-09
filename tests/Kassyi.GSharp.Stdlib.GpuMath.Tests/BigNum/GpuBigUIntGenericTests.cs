using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigUIntGenericTests
{
    [Fact]
    public void Test_GpuBigUInt_Constants()
    {
        var zero = GpuBigUInt<LimbBuffer4>.Zero;
        Assert.True(zero.IsZero());

        var one = GpuBigUInt<LimbBuffer4>.One;
        Assert.False(one.IsZero());
        Assert.Equal(1u, one.Limbs[0]);
        Assert.Equal(0u, one.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Addition()
    {
        var a = GpuBigUInt<LimbBuffer4>.One;
        var b = GpuBigUInt<LimbBuffer4>.One;
        var c = a + b;
        
        Assert.Equal(2u, c.Limbs[0]);
        Assert.Equal(0u, c.Limbs[1]);
        
        // Test carry
        a.Limbs[0] = uint.MaxValue;
        b.Limbs[0] = 1;
        var d = a + b;
        
        Assert.Equal(0u, d.Limbs[0]);
        Assert.Equal(1u, d.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Subtraction()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 3;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        var c = a - b;
        
        Assert.Equal(2u, c.Limbs[0]);
        Assert.Equal(0u, c.Limbs[1]);
        
        // Test borrow
        a.Limbs[0] = 0;
        a.Limbs[1] = 1;
        b.Limbs[0] = 1;
        b.Limbs[1] = 0;
        var d = a - b;
        
        Assert.Equal(uint.MaxValue, d.Limbs[0]);
        Assert.Equal(0u, d.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Comparison()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 3;
        var cl = new LimbBuffer4(); cl[0] = 5;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        var c = new GpuBigUInt<LimbBuffer4>(cl);
        
        Assert.True(a > b);
        Assert.True(a >= b);
        Assert.True(b < a);
        Assert.True(b <= a);
        Assert.True(a == c);
        Assert.True(a >= c);
        Assert.True(a <= c);
        Assert.True(a != b);
    }

    [Fact]
    public void Test_GpuBigUInt_ShiftLeft()
    {
        var al = new LimbBuffer4(); al[0] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = a.ShiftLeft(1);
        Assert.Equal(2u, b.Limbs[0]);
        
        var c = a.ShiftLeft(32);
        Assert.Equal(0u, c.Limbs[0]);
        Assert.Equal(1u, c.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_ShiftRight()
    {
        var al = new LimbBuffer4(); al[0] = 0; al[1] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = a.ShiftRight(32, out bool stickyBit);
        Assert.Equal(1u, b.Limbs[0]);
        Assert.Equal(0u, b.Limbs[1]);
        Assert.False(stickyBit);
    }

    [Fact]
    public void Test_GpuBigUInt_SetMsb()
    {
        var a = new GpuBigUInt<LimbBuffer4>();
        var b = a.SetMsb();
        Assert.Equal(0x80000000u, b.Limbs[3]);
    }

    [Fact]
    public void Test_GpuBigUInt_IsOdd()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 4;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        
        Assert.True(a.IsOdd());
        Assert.False(b.IsOdd());
    }

    [Fact]
    public void Test_GpuBigUInt_CountLeadingZeros()
    {
        var al = new LimbBuffer4(); al[0] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        int leading = a.CountLeadingZeros();
        Assert.Equal(127, leading); // 4 * 32 - 1
    }
}
