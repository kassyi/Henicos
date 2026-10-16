using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatTests
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, false, false)] // (+10) + (+2) = +12
    [InlineData(true, true, true)]    // (-10) + (-2) = -12
    public void Test_SignRules_Addition(bool isNegA, bool isNegB, bool expectedIsNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var sigA = new GpuBigUInt(context, new uint[] { 10, 0, 0, 0 });
        using var sigB = new GpuBigUInt(context, new uint[] { 2, 0, 0, 0 });

        var floatA = new GpuBigFloat(sigA, 5, isNegA);
        var floatB = new GpuBigFloat(sigB, 5, isNegB);

        GpuBigFloat result = floatA + floatB;
        Assert.Equal(expectedIsNeg, result.IsNegative);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Test_ExponentArithmetic_BoundaryValues(int exp)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var sig = new GpuBigUInt(context, new uint[] { 0xAAAAAAAA, 0x55555555, 0xFFFFFFFF, 0x80000000 });
        var floatVal = new GpuBigFloat(sig, exp, isNegative: false);
        
        // Assert creation preserves exponent boundaries without corrupting significand
        Assert.Equal(exp, floatVal.UFloat.Exponent);
        Assert.Equal(0xAAAAAAAA, floatVal.UFloat.Significand.ToHostArray()[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroSignificandNormalization_FlushesToZeroExponent()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var zeroSig = new GpuBigUInt(context, new uint[] { 0, 0, 0, 0 });
        using var valSig = new GpuBigUInt(context, new uint[] { 42, 0, 0, 0 });

        var floatZero = new GpuBigFloat(zeroSig, 100, isNegative: true);
        var floatVal = new GpuBigFloat(valSig, 50, isNegative: false);

        var normZero = floatZero.Normalize();
        Assert.True(normZero.UFloat.Significand.IsZero());
        Assert.Equal(0, normZero.UFloat.Exponent);
        
        var normVal = floatVal.Normalize();
        Assert.False(normVal.UFloat.Significand.IsZero());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DivisionByZeroSignificand_ThrowsOrDetectsZeroDivisor()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var valSig = new GpuBigUInt(context, new uint[] { 42, 0, 0, 0 });
        using var zeroSig = new GpuBigUInt(context, new uint[] { 0, 0, 0, 0 });
        var floatVal = new GpuBigFloat(valSig, 0, isNegative: false);
        var floatZero = new GpuBigFloat(zeroSig, 0, isNegative: false);

        Assert.True(floatZero.UFloat.Significand.IsZero());

        Assert.Throws<DivideByZeroException>(() =>
        {
            using GpuBigFloat _ = floatVal / floatZero;
        });
    }
}

