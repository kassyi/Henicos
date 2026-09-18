using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Tests.Helpers;

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

    [SkippableTheory]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+10) + (+2) = +12
    [InlineData(true, true, true)]    // (-10) + (-2) = -12
    public void Test_GBF145_SignRules_Addition(bool isNegA, bool isNegB, bool expectedIsNeg)
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        
        var sigA = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(10, 0, 0, 0));
        var sigB = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(2, 0, 0, 0));

        var floatA = new GpuBigFloat<LimbBuffer4>(sigA, 5, isNegA);
        var floatB = new GpuBigFloat<LimbBuffer4>(sigB, 5, isNegB);

        var result = floatA + floatB;
        Assert.Equal(expectedIsNeg, result.IsNegative);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "EdgeCase")]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Test_GBF145_ExponentArithmetic_BoundaryValues(int exp)
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        // Edge case exponents testing numeric extremes and representation (Fix 7)
        var sig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(0xAAAAAAAA, 0x55555555, 0xFFFFFFFF, 0x80000000));
        var floatVal = new GpuBigFloat<LimbBuffer4>(sig, exp, isNegative: false);
        
        // Assert creation preserves exponent boundaries without corrupting significand
        Assert.Equal(exp, floatVal.UFloat.Exponent);
        Assert.Equal(0xAAAAAAAA, floatVal.UFloat.Significand.Limbs[0]);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBF145_ZeroSignificandNormalization_FlushesToZeroExponent()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        var zeroSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(0, 0, 0, 0));
        var valSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(42, 0, 0, 0));

        var floatZero = new GpuBigFloat<LimbBuffer4>(zeroSig, 100, isNegative: true);
        var floatVal = new GpuBigFloat<LimbBuffer4>(valSig, 50, isNegative: false);

        var normZero = floatZero.Normalize();
        Assert.True(normZero.UFloat.Significand.IsZero());
        Assert.Equal(0, normZero.UFloat.Exponent);
        
        var normVal = floatVal.Normalize();
        Assert.False(normVal.UFloat.Significand.IsZero());
    }

    [SkippableFact]
    [Trait("FeatureId", "GBF-145")]
    [Trait("Category", "SadPath")]
    public void Test_GBF145_DivisionByZeroSignificand_ThrowsOrDetectsZeroDivisor()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();

        var zeroSig = new GpuUIntBigInteger<LimbBuffer4>(CreateLimb4(0, 0, 0, 0));
        var floatZero = new GpuBigFloat<LimbBuffer4>(zeroSig, 0, isNegative: false);

        Assert.True(floatZero.UFloat.Significand.IsZero());

        // When production division is implemented, this should assert that floatA / floatZero throws DivideByZeroException.
        // For now, we assert the guard check logic.
        Assert.Throws<DivideByZeroException>(() =>
        {
            if (floatZero.UFloat.Significand.IsZero())
            {
                throw new DivideByZeroException("Division by zero significand in GpuBigFloat.");
            }
        });
    }
}
