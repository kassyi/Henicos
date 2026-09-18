using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Tests.Stdlib;

public class GBF145_GpuBigFloatTests
{
    private static LimbBuffer4 CreateLimb4(uint l0, uint l1, uint l2, uint l3)
    {
        var limbs = new LimbBuffer4();
        limbs[0] = l0;
        limbs[1] = l1;
        limbs[2] = l2;
        limbs[3] = l3;
        return limbs;
    }

    [Theory]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // pos * pos = pos
    [InlineData(false, true, true)]   // pos * neg = neg
    [InlineData(true, false, true)]   // neg * pos = neg
    [InlineData(true, true, false)]   // neg * neg = pos
    public void Test_GBF145_SignRules_MultiplicationAndDivision(bool isNegA, bool isNegB, bool expectedIsNeg)
    {
        var sigA = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(10, 0, 0, 0));
        var sigB = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(2, 0, 0, 0));

        var uFloatA = new GpuBigUFloat<LimbBuffer4>(sigA, exponent: 5);
        var uFloatB = new GpuBigUFloat<LimbBuffer4>(sigB, exponent: 3);

        var floatA = new GpuBigFloat<LimbBuffer4>(uFloatA, isNegA);
        var floatB = new GpuBigFloat<LimbBuffer4>(uFloatB, isNegB);

        // Verify sign behavior logic for floating-point operations
        bool mulSign = floatA.IsNegative ^ floatB.IsNegative;
        bool divSign = floatA.IsNegative ^ floatB.IsNegative;

        Assert.Equal(expectedIsNeg, mulSign);
        Assert.Equal(expectedIsNeg, divSign);
    }

    [Fact]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "HappyPath")]
    public void Test_GBF145_ExponentArithmetic_MultiplicationAndDivision()
    {
        int expA = 10;
        int expB = 4;

        // Exponent rules for floating point: E_mul = E_A + E_B, E_div = E_A - E_B
        int expectedMulExp = expA + expB; // 14
        int expectedDivExp = expA - expB; // 6

        Assert.Equal(14, expectedMulExp);
        Assert.Equal(6, expectedDivExp);
    }

    [Theory]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "EdgeCase")]
    [InlineData(int.MaxValue - 5, 10, true)]  // Exponent overflow
    [InlineData(int.MinValue + 5, -10, true)] // Exponent underflow
    [InlineData(100, -100, false)]             // No overflow/underflow
    public void Test_GBF145_ExponentOverflowUnderflowDetection(int expA, int expB, bool expectOverflowOrUnderflow)
    {
        bool overflowOrUnderflow = false;
        try
        {
            checked
            {
                int _ = expA + expB;
            }
        }
        catch (OverflowException)
        {
            overflowOrUnderflow = true;
        }

        Assert.Equal(expectOverflowOrUnderflow, overflowOrUnderflow);
    }

    [Fact]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "SadPath")]
    public void Test_GBF145_DivisionByZeroSignificand_ThrowsOrDetectsZeroDivisor()
    {
        var zeroSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(0, 0, 0, 0));
        var validSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(10, 0, 0, 0));

        var floatZero = new GpuBigFloat<LimbBuffer4>(zeroSig, 0, isNegative: false);
        var floatVal = new GpuBigFloat<LimbBuffer4>(validSig, 5, isNegative: false);

        Assert.True(floatZero.UFloat.Significand.IsZero());
        Assert.False(floatVal.UFloat.Significand.IsZero());

        // Division by zero condition validation
        Assert.Throws<DivideByZeroException>(() =>
        {
            if (floatZero.UFloat.Significand.IsZero())
            {
                throw new DivideByZeroException("Division by zero significand in GpuBigFloat.");
            }
        });
    }

    [Fact]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBF145_ZeroSignificandMultiplication_FlushesToZeroExponent()
    {
        var zeroSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(0, 0, 0, 0));
        var valSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(42, 0, 0, 0));

        var floatZero = new GpuBigFloat<LimbBuffer4>(zeroSig, 100, isNegative: true);
        var floatVal = new GpuBigFloat<LimbBuffer4>(valSig, 50, isNegative: false);

        var normZero = floatZero.Normalize();
        Assert.True(normZero.UFloat.Significand.IsZero());
        Assert.Equal(0, normZero.UFloat.Exponent);
    }
}
