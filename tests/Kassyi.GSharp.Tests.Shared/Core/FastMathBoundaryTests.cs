using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Trait("IssueId", "177")]
public class FastMathBoundaryTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HostSide_FastMath_FallsBackToMathF()
    {
        // Assert that invoking FastMath API directly on CPU falls back to MathF
        Assert.Equal(MathF.Exp(1.0f), FastMath.Exp(1.0f));
        Assert.Equal(MathF.Log(1.0f), FastMath.Log(1.0f));
        Assert.Equal(MathF.Sqrt(1.0f), FastMath.Sqrt(1.0f));
        Assert.Equal(MathF.Abs(-1.0f), FastMath.Abs(-1.0f));
    }

    public static TheoryData<float> GetBoundaryValues() =>
    [
        0.0f,
        -0.0f,
        1.0f,
        -1.0f,
        float.MaxValue,
        float.MinValue,
        float.Epsilon,
        float.NaN,
        float.PositiveInfinity,
        float.NegativeInfinity,
        BitConverter.Int32BitsToSingle(0x00400000), // Subnormal
        BitConverter.Int32BitsToSingle(unchecked((int)0x80400000)), // Negative subnormal
    ];

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [MemberData(nameof(GetBoundaryValues))]
    public void Test_GpuSide_FastMath_Boundaries(float inputVal)
    {
        // 1. Arrange
        float[] input = [inputVal];
        float[] outputExp = new float[1];
        float[] outputLog = new float[1];
        float[] outputSqrt = new float[1];
        float[] outputAbs = new float[1];

        // 2. Act
        FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Exp(input, outputExp);
        FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Log(input, outputLog);
        FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Sqrt(input, outputSqrt);
        FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Abs(input, outputAbs);

        // 3. Assert all 4 operations across all 12 boundary inputs
        if (float.IsNaN(inputVal))
        {
            Assert.True(float.IsNaN(outputAbs[0]));
            Assert.True(float.IsNaN(outputExp[0]));
            Assert.True(float.IsNaN(outputLog[0]));
            Assert.True(float.IsNaN(outputSqrt[0]));
        }
        else if (float.IsPositiveInfinity(inputVal))
        {
            Assert.True(float.IsPositiveInfinity(outputAbs[0]));
            Assert.True(float.IsPositiveInfinity(outputExp[0]));
            Assert.True(float.IsPositiveInfinity(outputLog[0]));
            Assert.True(float.IsPositiveInfinity(outputSqrt[0]));
        }
        else if (float.IsNegativeInfinity(inputVal))
        {
            Assert.True(float.IsPositiveInfinity(outputAbs[0]));
            Assert.Equal(0.0f, outputExp[0]);
            Assert.True(float.IsNaN(outputLog[0]));
            Assert.True(float.IsNaN(outputSqrt[0]));
        }
        else if (inputVal == 0.0f)
        {
            Assert.Equal(0.0f, outputAbs[0]);
            Assert.Equal(1.0f, outputExp[0], 1e-5f);
            Assert.True(float.IsNegativeInfinity(outputLog[0]));
            Assert.Equal(0.0f, outputSqrt[0]);
        }
        else if (inputVal == 1.0f)
        {
            Assert.Equal(1.0f, outputAbs[0]);
            Assert.Equal(MathF.E, outputExp[0], 1e-4f);
            Assert.Equal(0.0f, outputLog[0], 1e-5f);
            Assert.Equal(1.0f, outputSqrt[0], 1e-5f);
        }
        else if (inputVal == -1.0f)
        {
            Assert.Equal(1.0f, outputAbs[0]);
            Assert.Equal(1.0f / MathF.E, outputExp[0], 1e-4f);
            Assert.True(float.IsNaN(outputLog[0]));
            Assert.True(float.IsNaN(outputSqrt[0]));
        }
        else if (inputVal == float.MaxValue)
        {
            Assert.Equal(float.MaxValue, outputAbs[0]);
            Assert.True(float.IsPositiveInfinity(outputExp[0]));
            Assert.True(outputLog[0] > 88.0f && !float.IsNaN(outputLog[0]));
            Assert.True(outputSqrt[0] > 1e19f && !float.IsNaN(outputSqrt[0]));
        }
        else if (inputVal == float.MinValue)
        {
            Assert.Equal(float.MaxValue, outputAbs[0]);
            Assert.Equal(0.0f, outputExp[0]);
            Assert.True(float.IsNaN(outputLog[0]));
            Assert.True(float.IsNaN(outputSqrt[0]));
        }
        else if (inputVal > 0.0f)
        {
            // Positive subnormal / Epsilon
            Assert.True(outputAbs[0] >= 0.0f);
            Assert.Equal(1.0f, outputExp[0], 1e-4f);
            Assert.True(outputLog[0] < 0.0f);
            Assert.True(outputSqrt[0] >= 0.0f);
        }
        else
        {
            // Negative subnormal
            Assert.True(outputAbs[0] >= 0.0f);
            Assert.Equal(1.0f, outputExp[0], 1e-4f);
            Assert.True(float.IsNaN(outputLog[0]));
            Assert.True(float.IsNaN(outputSqrt[0]));
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_GpuSide_FastMath_EmptyInputs()
    {
        // Empty array arguments are rejected instead of silently skipping the launch
        float[] empty = [];
        Assert.Equal("input", Assert.Throws<ArgumentException>(() =>
            FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Exp(empty, empty)).ParamName);
        Assert.Equal("input", Assert.Throws<ArgumentException>(() =>
            FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Log(empty, empty)).ParamName);
        Assert.Equal("input", Assert.Throws<ArgumentException>(() =>
            FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Sqrt(empty, empty)).ParamName);
        Assert.Equal("input", Assert.Throws<ArgumentException>(() =>
            FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Abs(empty, empty)).ParamName);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stress)]
    public void Test_GpuSide_FastMath_Stress()
    {
        const int iterations = 100;

        Parallel.For(0, iterations, i =>
        {
            float[] input = [(float)i];
            float[] output = new float[1];

            // JIT Engine should handle parallel concurrent launches cleanly
            FastMathBoundaryKernels.LaunchKernel_COR01_FastMath_Exp(input, output);

            Assert.True(output[0] >= 1.0f); // e^x (x>=0) is always >= 1.0
        });
    }
}
