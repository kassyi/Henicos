using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels for <see cref="MathMaxMinScenarios"/>: each thread applies every floating-point <c>Max</c> / <c>Min</c> /
/// <c>MaxNumber</c> / <c>MinNumber</c> / <c>Clamp</c> form to its own inputs, so one launch covers one input row per
/// thread (#726).
/// </summary>
public static partial class MathMaxMinKernels
{
    [GpuKernel]
    public static void SingleMaxMinKernel(float[] left, float[] right, float[] result)
    {
        int i = ThreadIndex.X;
        int row = i * 8;
        result[row] = Math.Max(left[i], right[i]);
        result[row + 1] = Math.Min(left[i], right[i]);
        result[row + 2] = MathF.Max(left[i], right[i]);
        result[row + 3] = MathF.Min(left[i], right[i]);
        result[row + 4] = float.Max(left[i], right[i]);
        result[row + 5] = float.Min(left[i], right[i]);
        result[row + 6] = float.MaxNumber(left[i], right[i]);
        result[row + 7] = float.MinNumber(left[i], right[i]);
    }

    [GpuKernel]
    public static void DoubleMaxMinKernel(double[] left, double[] right, double[] result)
    {
        int i = ThreadIndex.X;
        int row = i * 6;
        result[row] = Math.Max(left[i], right[i]);
        result[row + 1] = Math.Min(left[i], right[i]);
        result[row + 2] = double.Max(left[i], right[i]);
        result[row + 3] = double.Min(left[i], right[i]);
        result[row + 4] = double.MaxNumber(left[i], right[i]);
        result[row + 5] = double.MinNumber(left[i], right[i]);
    }

    [GpuKernel]
    public static void SingleClampKernel(float[] value, float[] min, float[] max, float[] result)
    {
        int i = ThreadIndex.X;
        result[i] = Math.Clamp(value[i], min[i], max[i]);
    }

    [GpuKernel]
    public static void DoubleClampKernel(double[] value, double[] min, double[] max, double[] result)
    {
        int i = ThreadIndex.X;
        result[i] = Math.Clamp(value[i], min[i], max[i]);
    }

    [GpuKernel]
    public static void MixedOperandMaxKernel(float[] single, double[] wide, int[] whole, float[] singleResult, double[] wideResult)
    {
        // Implicit conversions of the operands pick the overload, as in C#
        singleResult[0] = Math.Max(single[0], whole[0]);
        singleResult[1] = Math.Min(single[0], 2);
        wideResult[0] = Math.Max(wide[0], single[0]);
        wideResult[1] = Math.Min(wide[0], whole[0]);
    }
}
