using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.FastMathTests;

[Trait("FeatureId", "COR-01")]
[Trait("IssueId", "177")]
public class COR_01_FastMathBoundaryTests
{
    [Fact]
    [Trait("Category", "SadPath")]
    public void Test_COR01_HostSide_FastMath_ThrowsGpuOnlyException_SadPath()
    {
        // Assert that invoking FastMath API directly on CPU throws GpuOnlyException
        Assert.Throws<GpuOnlyException>(() => FastMath.Exp(1.0f));
        Assert.Throws<GpuOnlyException>(() => FastMath.Log(1.0f));
        Assert.Throws<GpuOnlyException>(() => FastMath.Sqrt(1.0f));
        Assert.Throws<GpuOnlyException>(() => FastMath.Abs(-1.0f));
    }

    public static IEnumerable<object[]> GetBoundaryValues()
    {
        yield return [0.0f];
        yield return [-0.0f];
        yield return [1.0f];
        yield return [-1.0f];
        yield return [float.MaxValue];
        yield return [float.MinValue];
        yield return [float.Epsilon];
        yield return [float.NaN];
        yield return [float.PositiveInfinity];
        yield return [float.NegativeInfinity];
        yield return [BitConverter.Int32BitsToSingle(0x00400000)]; // Subnormal
        yield return [BitConverter.Int32BitsToSingle(unchecked((int)0x80400000))]; // Negative subnormal
    }

    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [MemberData(nameof(GetBoundaryValues))]
    public void Test_COR01_GpuSide_FastMath_Boundaries(float inputVal)
    {
        // 1. Arrange
        float[] input = [inputVal];
        float[] outputExp = new float[1];
        float[] outputLog = new float[1];
        float[] outputSqrt = new float[1];
        float[] outputAbs = new float[1];

        // 2. Act
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Exp(input, outputExp);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Log(input, outputLog);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Sqrt(input, outputSqrt);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Abs(input, outputAbs);

        // 3. Assert
        // In boundary test we primarily care that it doesn't crash the GPU/Runtime
        // FastMath intrinsics on GPU have different precision/NaN handling than CPU MathF,
        // so strict IEEE-754 equality is not enforced here, but basic safety and mapping is verified.

        if (inputVal == -1.0f)
        {
            Assert.Equal(1.0f, outputAbs[0]);
        }
        else if (inputVal == 0.0f)
        {
            Assert.Equal(0.0f, outputAbs[0]);
            Assert.Equal(1.0f, outputExp[0]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void Test_COR01_GpuSide_FastMath_EmptyInputs()
    {
        float[] empty = [];
        // Should not throw or crash
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Exp(empty, empty);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Log(empty, empty);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Sqrt(empty, empty);
        COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Abs(empty, empty);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("Category", "Stress")]
    public void Test_COR01_GpuSide_FastMath_Stress()
    {
        const int iterations = 100;

        Parallel.For(0, iterations, i =>
        {
            float[] input = [(float)i];
            float[] output = new float[1];

            // JIT Engine should handle parallel concurrent launches cleanly
            COR_01_FastMathBoundaryKernelsWrapper.Launch_Kernel_COR01_FastMath_Exp(input, output);

            Assert.True(output[0] >= 1.0f); // e^x (x>=0) is always >= 1.0
        });
    }
}
