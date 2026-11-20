using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches <see cref="ConstZeroDivisorKernels"/> and checks the infinities and NaNs that C# defines, for the native
/// CPU, the managed CPU and the CUDA paths. The payload of a NaN is not compared.
/// </summary>
internal static class ConstZeroDivisorScenarios
{
    private const int ThreadCount = 2;

    public static void ClassConst(Action<Action> run)
    {
        var doubles = new double[ThreadCount * 4];
        var floats = new float[ThreadCount * 2];

        run(() => ConstZeroDivisorKernels.LaunchClassConstZeroDivisorKernel(doubles, floats, blockDimX: ThreadCount));

        AssertDivisions(doubles, floats);
    }

    public static void LocalConst(Action<Action> run)
    {
        var doubles = new double[ThreadCount * 4];
        var floats = new float[ThreadCount * 2];

        run(() => ConstZeroDivisorKernels.LaunchLocalConstZeroDivisorKernel(doubles, floats, blockDimX: ThreadCount));

        AssertDivisions(doubles, floats);
    }

    public static void NonConstantDividend(Action<Action> run)
    {
        double[] x = [1.0, 1.0];
        var divided = new double[ThreadCount];
        var assigned = new double[ThreadCount];

        run(() => ConstZeroDivisorKernels.LaunchNonConstantDividendKernel(x, divided, assigned,
            blockDimX: ThreadCount));

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.True(double.IsNegativeInfinity(divided[i]));
            Assert.True(double.IsPositiveInfinity(assigned[i]));
        }
    }

    private static void AssertDivisions(double[] doubles, float[] floats)
    {
        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.True(double.IsNegativeInfinity(doubles[i * 4 + 0]));
            Assert.True(double.IsPositiveInfinity(doubles[i * 4 + 1]));
            Assert.True(double.IsNaN(doubles[i * 4 + 2]));
            Assert.True(double.IsNegativeInfinity(doubles[i * 4 + 3]));
            Assert.True(float.IsNegativeInfinity(floats[i * 2 + 0]));
            Assert.True(float.IsNegativeInfinity(floats[i * 2 + 1]));
        }
    }
}
