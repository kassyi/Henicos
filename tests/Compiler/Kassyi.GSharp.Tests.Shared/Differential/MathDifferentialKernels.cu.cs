// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for <see cref="Math"/>, <see cref="MathF"/>, <see cref="float"/> and <see cref="double"/>
/// intrinsics mapped in <c>StandardCpp.yml</c> and <c>GSharpCuda.yml</c>.
/// </summary>
public static partial class MathDifferentialKernels
{
    /// <summary>The columns of <see cref="MathOfSingleMatrixKernel"/>.</summary>
    public static readonly string[] MathOfSingleColumns =
    [
        "Math.Sqrt(f)",
        "Math.Sqrt(f)*3.0",
        "Math.Floor(f)",
        "Math.Ceiling(f)",
        "Math.Round(f)",
        "Math.Round(f)/3.0",
        "Math.Floor(f)/3",
        "Math.Ceiling(f)/3",
        "Math.Round(f)/3",
        "Math.Abs(f)",
    ];

    /// <summary>The columns of <see cref="SingleRoundingAndUnaryMatrixKernel"/>.</summary>
    public static readonly string[] SingleRoundingAndUnaryColumns =
    [
        "MathF.Round(f)",
        "MathF.Floor(f)",
        "MathF.Ceiling(f)",
        "MathF.Abs(f)",
        "MathF.Sqrt(f)",
        "Math.Abs(f)",
    ];

    /// <summary>The columns of <see cref="DoubleRoundingAndUnaryMatrixKernel"/>.</summary>
    public static readonly string[] DoubleRoundingAndUnaryColumns =
    [
        "Math.Round(d)",
        "Math.Floor(d)",
        "Math.Ceiling(d)",
        "Math.Abs(d)",
        "Math.Sqrt(d)",
    ];

    /// <summary>The columns of <see cref="SingleMaxMinClampMatrixKernel"/>.</summary>
    public static readonly string[] SingleMaxMinClampColumns =
    [
        "Math.Max(a,b)",
        "Math.Min(a,b)",
        "MathF.Max(a,b)",
        "MathF.Min(a,b)",
        "float.Max(a,b)",
        "float.Min(a,b)",
        "float.MaxNumber(a,b)",
        "float.MinNumber(a,b)",
        "Math.Clamp(a,min,max)",
    ];

    /// <summary>The columns of <see cref="DoubleMaxMinClampMatrixKernel"/>.</summary>
    public static readonly string[] DoubleMaxMinClampColumns =
    [
        "Math.Max(a,b)",
        "Math.Min(a,b)",
        "double.Max(a,b)",
        "double.Min(a,b)",
        "double.MaxNumber(a,b)",
        "double.MinNumber(a,b)",
        "Math.Clamp(a,min,max)",
    ];

    /// <summary>The <see cref="float"/> columns of <see cref="MixedFloatMaxMinMatrixKernel"/>.</summary>
    public static readonly string[] MixedSingleMaxMinColumns =
    [
        "Math.Max(f,i)",
        "Math.Min(f,i)",
        "float.Max(f,i)",
        "float.Min(f,i)",
    ];

    /// <summary>The <see cref="double"/> columns of <see cref="MixedFloatMaxMinMatrixKernel"/>.</summary>
    public static readonly string[] MixedDoubleMaxMinColumns =
    [
        "Math.Max(f,d)",
        "Math.Min(f,d)",
        "Math.Max(d,f)",
        "Math.Min(d,f)",
        "Math.Max(d,i)",
        "Math.Min(d,i)",
        "double.MaxNumber(f,d)",
        "double.MinNumber(f,d)",
    ];

    /// <summary>The columns of <see cref="Int32MathMatrixKernel"/>.</summary>
    public static readonly string[] Int32MathColumns =
    [
        "Math.Abs(a)",
        "Math.Abs((int)s)",
        "Math.Abs((int)sb)",
        "Math.Max(a,b)",
        "Math.Min(a,b)",
        "Math.Max(a,(int)s)",
        "Math.Min(a,(int)s)",
        "Math.Clamp(a,min,max)",
    ];

    /// <summary>The <see cref="float"/> columns of <see cref="MathFIntArgumentMatrixKernel"/>.</summary>
    public static readonly string[] MathFIntSingleColumns =
    [
        "MathF.Abs(v)",
        "MathF.Round(v)",
        "MathF.Max(v,min)",
        "MathF.Min(v,max)",
    ];

    // System.Math (double) methods called with a float argument: C# widens f to double and calls the double overload,
    // whereas C++ overload resolution on unqualified sqrt / floor / ceil / rint selects the float overload
    [GpuKernel]
    public static void MathOfSingleMatrixKernel(float[] f, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        int o = t * 10;
        r[o] = Math.Sqrt(f[t]);
        r[o + 1] = Math.Sqrt(f[t]) * 3.0;
        r[o + 2] = Math.Floor(f[t]);
        r[o + 3] = Math.Ceiling(f[t]);
        r[o + 4] = Math.Round(f[t]);
        r[o + 5] = Math.Round(f[t]) / 3.0;
        r[o + 6] = Math.Floor(f[t]) / 3;
        r[o + 7] = Math.Ceiling(f[t]) / 3;
        r[o + 8] = Math.Round(f[t]) / 3;
        r[o + 9] = Math.Abs(f[t]);
    }

    [GpuKernel]
    public static void SingleRoundingAndUnaryMatrixKernel(float[] f, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        int o = t * 6;
        r[o] = MathF.Round(f[t]);
        r[o + 1] = MathF.Floor(f[t]);
        r[o + 2] = MathF.Ceiling(f[t]);
        r[o + 3] = MathF.Abs(f[t]);
        r[o + 4] = MathF.Sqrt(f[t]);
        r[o + 5] = Math.Abs(f[t]);
    }

    [GpuKernel]
    public static void DoubleRoundingAndUnaryMatrixKernel(double[] d, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= d.Length)
        {
            return;
        }

        int o = t * 5;
        r[o] = Math.Round(d[t]);
        r[o + 1] = Math.Floor(d[t]);
        r[o + 2] = Math.Ceiling(d[t]);
        r[o + 3] = Math.Abs(d[t]);
        r[o + 4] = Math.Sqrt(d[t]);
    }

    [GpuKernel]
    public static void SingleMaxMinClampMatrixKernel(float[] a, float[] b, float[] min, float[] max, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 9;
        r[o] = Math.Max(a[t], b[t]);
        r[o + 1] = Math.Min(a[t], b[t]);
        r[o + 2] = MathF.Max(a[t], b[t]);
        r[o + 3] = MathF.Min(a[t], b[t]);
        r[o + 4] = float.Max(a[t], b[t]);
        r[o + 5] = float.Min(a[t], b[t]);
        r[o + 6] = float.MaxNumber(a[t], b[t]);
        r[o + 7] = float.MinNumber(a[t], b[t]);
        r[o + 8] = Math.Clamp(a[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void DoubleMaxMinClampMatrixKernel(double[] a, double[] b, double[] min, double[] max, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 7;
        r[o] = Math.Max(a[t], b[t]);
        r[o + 1] = Math.Min(a[t], b[t]);
        r[o + 2] = double.Max(a[t], b[t]);
        r[o + 3] = double.Min(a[t], b[t]);
        r[o + 4] = double.MaxNumber(a[t], b[t]);
        r[o + 5] = double.MinNumber(a[t], b[t]);
        r[o + 6] = Math.Clamp(a[t], min[t], max[t]);
    }

    [GpuKernel]
    public static void MixedFloatMaxMinMatrixKernel(float[] f, double[] d, int[] iv, float[] rf, double[] rd)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= f.Length)
        {
            return;
        }

        int of = t * 4;
        rf[of] = Math.Max(f[t], iv[t]);
        rf[of + 1] = Math.Min(f[t], iv[t]);
        rf[of + 2] = float.Max(f[t], iv[t]);
        rf[of + 3] = float.Min(f[t], iv[t]);

        int od = t * 8;
        rd[od] = Math.Max(f[t], d[t]);
        rd[od + 1] = Math.Min(f[t], d[t]);
        rd[od + 2] = Math.Max(d[t], f[t]);
        rd[od + 3] = Math.Min(d[t], f[t]);
        rd[od + 4] = Math.Max(d[t], iv[t]);
        rd[od + 5] = Math.Min(d[t], iv[t]);
        rd[od + 6] = double.MaxNumber(f[t], d[t]);
        rd[od + 7] = double.MinNumber(f[t], d[t]);
    }

    [GpuKernel]
    public static void Int32MathMatrixKernel(int[] a, int[] b, short[] s, sbyte[] sb, int[] min, int[] max, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 8;
        r[o] = Math.Abs(a[t]);
        r[o + 1] = Math.Abs((int)s[t]);
        r[o + 2] = Math.Abs((int)sb[t]);
        r[o + 3] = Math.Max(a[t], b[t]);
        r[o + 4] = Math.Min(a[t], b[t]);
        r[o + 5] = Math.Max(a[t], (int)s[t]);
        r[o + 6] = Math.Min(a[t], (int)s[t]);
        r[o + 7] = Math.Clamp(a[t], min[t], max[t]);
    }

    // MathF methods called with int arguments: C# implicitly converts int -> float and calls the float method
    [GpuKernel]
    public static void MathFIntArgumentMatrixKernel(int[] v, int[] min, int[] max, float[] rf)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= v.Length)
        {
            return;
        }

        int o = t * 4;
        rf[o] = MathF.Abs(v[t]);
        rf[o + 1] = MathF.Round(v[t]);
        rf[o + 2] = MathF.Max(v[t], min[t]);
        rf[o + 3] = MathF.Min(v[t], max[t]);
    }
}
