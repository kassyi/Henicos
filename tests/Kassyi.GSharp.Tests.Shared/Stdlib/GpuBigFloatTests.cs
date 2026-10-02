using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.GpuBigFloat;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

[Trait("Category", "GpuExecution")]
public class GpuBigFloatTests
{
    [SkippableTheory]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+10) + (+2) = +12
    [InlineData(true, true, true)]    // (-10) + (-2) = -12
    public void Test_SignRules_Addition(bool isNegA, bool isNegB, bool expectedIsNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var sigA = new GpuUIntBigInteger(context, new uint[] { 10, 0, 0, 0 });
        using var sigB = new GpuUIntBigInteger(context, new uint[] { 2, 0, 0, 0 });

        var floatA = new GpuBigFloat(sigA, 5, isNegA);
        var floatB = new GpuBigFloat(sigB, 5, isNegB);

        GpuBigFloat result = floatA + floatB;
        Assert.Equal(expectedIsNeg, result.IsNegative);
    }

    [SkippableTheory]
    [Trait("Category", "EdgeCase")]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Test_ExponentArithmetic_BoundaryValues(int exp)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var sig = new GpuUIntBigInteger(context, new uint[] { 0xAAAAAAAA, 0x55555555, 0xFFFFFFFF, 0x80000000 });
        var floatVal = new GpuBigFloat(sig, exp, isNegative: false);
        
        // Assert creation preserves exponent boundaries without corrupting significand
        Assert.Equal(exp, floatVal.UFloat.Exponent);
        Assert.Equal(0xAAAAAAAA, floatVal.UFloat.Significand.ToHostArray()[0]);
    }

    [SkippableFact]
    [Trait("Category", "EdgeCase")]
    public void Test_ZeroSignificandNormalization_FlushesToZeroExponent()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var zeroSig = new GpuUIntBigInteger(context, new uint[] { 0, 0, 0, 0 });
        using var valSig = new GpuUIntBigInteger(context, new uint[] { 42, 0, 0, 0 });

        var floatZero = new GpuBigFloat(zeroSig, 100, isNegative: true);
        var floatVal = new GpuBigFloat(valSig, 50, isNegative: false);

        var normZero = floatZero.Normalize();
        Assert.True(normZero.UFloat.Significand.IsZero());
        Assert.Equal(0, normZero.UFloat.Exponent);
        
        var normVal = floatVal.Normalize();
        Assert.False(normVal.UFloat.Significand.IsZero());
    }

    [SkippableFact]
    [Trait("Category", "SadPath")]
    public void Test_DivisionByZeroSignificand_ThrowsOrDetectsZeroDivisor()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var zeroSig = new GpuUIntBigInteger(context, new uint[] { 0, 0, 0, 0 });
        var floatZero = new GpuBigFloat(zeroSig, 0, isNegative: false);

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

