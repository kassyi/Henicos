using System;
using System.Collections.Generic;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="MathMaxMinKernels"/> (#726). The expected values come from the .NET
/// methods themselves, called on the host with the same inputs: <c>Math.Max</c> / <c>Min</c> return NaN when either
/// input is NaN and order +0 above -0 (IEEE 754:2019 maximum / minimum), <c>MaxNumber</c> / <c>MinNumber</c> return
/// the other input for one NaN (maximumNumber), and <c>Clamp</c> compares value with min first. Values are compared
/// bit for bit, except that any NaN matches any NaN (.NET returns the input NaN, PTX a canonical one). Every mismatch
/// is listed, so that one failure shows all the cases a change breaks.
/// </summary>
internal static class MathMaxMinScenarios
{
    private static readonly (float Left, float Right)[] s_singlePairs =
    [
        (1f, 2f),
        (2f, 1f),
        (-3f, -4f),
        (5f, 5f),
        (float.NaN, 1f),
        (1f, float.NaN),
        (float.NaN, float.NaN),
        (-0f, 0f),
        (0f, -0f),
        (-0f, -0f),
        (float.PositiveInfinity, 1f),
        (float.NegativeInfinity, float.NaN),
    ];

    private static readonly (double Left, double Right)[] s_doublePairs =
    [
        (1d, 2d),
        (2d, 1d),
        (-3d, -4d),
        (5d, 5d),
        (double.NaN, 1d),
        (1d, double.NaN),
        (double.NaN, double.NaN),
        (-0d, 0d),
        (0d, -0d),
        (-0d, -0d),
        (double.PositiveInfinity, 1d),
        (double.NegativeInfinity, double.NaN),
    ];

    // (value, min, max) with min <= max or a NaN bound, so that .NET does not throw
    private static readonly (double Value, double Min, double Max)[] s_clampRows =
    [
        (0.5, 0, 1),
        (2, 0, 1),
        (-1, 0, 1),
        (double.NaN, 0, 1),
        (-0d, 0, 1),
        (0, -0d, 1),
        (0.5, double.NaN, 1),
        (0.5, 0, double.NaN),
        (1, 0, 1),
        (0, 0, 1),
    ];

    private static readonly string[] s_singleOperations =
        ["Math.Max", "Math.Min", "MathF.Max", "MathF.Min", "float.Max", "float.Min", "float.MaxNumber", "float.MinNumber"];

    private static readonly string[] s_doubleOperations =
        ["Math.Max", "Math.Min", "double.Max", "double.Min", "double.MaxNumber", "double.MinNumber"];

    public static void SingleMaxMin(Action<Action> run)
    {
        int count = s_singlePairs.Length;
        var left = new float[count];
        var right = new float[count];
        for (int i = 0; i < count; i++)
        {
            (left[i], right[i]) = s_singlePairs[i];
        }

        var result = new float[count * s_singleOperations.Length];

        run(() => MathMaxMinKernels.LaunchSingleMaxMinKernel(left, right, result, blockDimX: (uint)count));

        var mismatches = new List<string>();

        for (int i = 0; i < count; i++)
        {
            float a = left[i];
            float b = right[i];
            float[] expected =
                [Math.Max(a, b), Math.Min(a, b), MathF.Max(a, b), MathF.Min(a, b), float.Max(a, b), float.Min(a, b), float.MaxNumber(a, b), float.MinNumber(a, b)];
            for (int k = 0; k < expected.Length; k++)
            {
                Check(mismatches, expected[k], result[i * expected.Length + k], $"{s_singleOperations[k]}({Show(a)}, {Show(b)})");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    public static void DoubleMaxMin(Action<Action> run)
    {
        int count = s_doublePairs.Length;
        var left = new double[count];
        var right = new double[count];
        for (int i = 0; i < count; i++)
        {
            (left[i], right[i]) = s_doublePairs[i];
        }

        var result = new double[count * s_doubleOperations.Length];

        run(() => MathMaxMinKernels.LaunchDoubleMaxMinKernel(left, right, result, blockDimX: (uint)count));

        var mismatches = new List<string>();

        for (int i = 0; i < count; i++)
        {
            double a = left[i];
            double b = right[i];
            double[] expected =
                [Math.Max(a, b), Math.Min(a, b), double.Max(a, b), double.Min(a, b), double.MaxNumber(a, b), double.MinNumber(a, b)];
            for (int k = 0; k < expected.Length; k++)
            {
                Check(mismatches, expected[k], result[i * expected.Length + k], $"{s_doubleOperations[k]}({Show(a)}, {Show(b)})");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    public static void SingleClamp(Action<Action> run)
    {
        int count = s_clampRows.Length;
        var value = new float[count];
        var min = new float[count];
        var max = new float[count];
        for (int i = 0; i < count; i++)
        {
            value[i] = (float)s_clampRows[i].Value;
            min[i] = (float)s_clampRows[i].Min;
            max[i] = (float)s_clampRows[i].Max;
        }

        var result = new float[count];

        run(() => MathMaxMinKernels.LaunchSingleClampKernel(value, min, max, result, blockDimX: (uint)count));

        var mismatches = new List<string>();

        for (int i = 0; i < count; i++)
        {
            Check(mismatches, Math.Clamp(value[i], min[i], max[i]), result[i], $"Math.Clamp({Show(value[i])}, {Show(min[i])}, {Show(max[i])})");
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    public static void DoubleClamp(Action<Action> run)
    {
        int count = s_clampRows.Length;
        var value = new double[count];
        var min = new double[count];
        var max = new double[count];
        for (int i = 0; i < count; i++)
        {
            (value[i], min[i], max[i]) = s_clampRows[i];
        }

        var result = new double[count];

        run(() => MathMaxMinKernels.LaunchDoubleClampKernel(value, min, max, result, blockDimX: (uint)count));

        var mismatches = new List<string>();

        for (int i = 0; i < count; i++)
        {
            Check(mismatches, Math.Clamp(value[i], min[i], max[i]), result[i], $"Math.Clamp({Show(value[i])}, {Show(min[i])}, {Show(max[i])})");
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    public static void MixedOperands(Action<Action> run)
    {
        float[] single = [1.5f];
        double[] wide = [-2.25];
        int[] whole = [3];
        var singleResult = new float[2];
        var wideResult = new double[2];

        run(() => MathMaxMinKernels.LaunchMixedOperandMaxKernel(single, wide, whole, singleResult, wideResult, blockDimX: 1));

        Assert.Equal(Math.Max(single[0], whole[0]), singleResult[0]);
        Assert.Equal(Math.Min(single[0], 2), singleResult[1]);
        Assert.Equal(Math.Max(wide[0], single[0]), wideResult[0]);
        Assert.Equal(Math.Min(wide[0], whole[0]), wideResult[1]);
    }

    private static void Check(List<string> mismatches, float expected, float actual, string label)
    {
        bool same = float.IsNaN(expected)
            ? float.IsNaN(actual)
            : BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);
        if (!same)
        {
            mismatches.Add($"{label}: expected {Show(expected)}, got {Show(actual)}");
        }
    }

    private static void Check(List<string> mismatches, double expected, double actual, string label)
    {
        bool same = double.IsNaN(expected)
            ? double.IsNaN(actual)
            : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);
        if (!same)
        {
            mismatches.Add($"{label}: expected {Show(expected)}, got {Show(actual)}");
        }
    }

    // Keeps the sign of zero visible in failure messages
    private static string Show(double value) =>
        value == 0 && double.IsNegative(value) ? "-0" : value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
