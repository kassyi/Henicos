// ReSharper disable All
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for <see cref="Math"/>, <see cref="MathF"/>, <see cref="FastMath"/>,
/// <see cref="BitOperations"/>, <see cref="float"/> and <see cref="double"/> intrinsics mapped in
/// <c>StandardCpp.yml</c>, <c>GSharpCuda.yml</c>, and <c>BclMappings.cs</c>.
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

    /// <summary>The <see cref="float"/> columns of <see cref="SubnormalIeeeExactMatrixKernel"/> (Case N01).</summary>
    public static readonly string[] SubnormalIeeeSingleColumns =
    [
        "x+y",
        "x-y",
        "x*y",
        "x/y",
        "MathF.Sqrt(|x|)",
        "FastMath.Sqrt(|x|)",
    ];

    /// <summary>The <see cref="double"/> columns of <see cref="SubnormalIeeeExactMatrixKernel"/> (Case N01).</summary>
    public static readonly string[] SubnormalIeeeDoubleColumns =
    [
        "u+v",
        "u-v",
        "u*v",
        "u/v",
        "Math.Sqrt(|u|)",
    ];

    /// <summary>The <see cref="float"/> columns of <see cref="TranscendentalSpecialValuesKernel"/> (Case N02).</summary>
    public static readonly string[] TranscendentalSpecialSingleColumns =
    [
        "MathF.Sin(fx)",
        "MathF.Cos(fx)",
        "MathF.Tan(fx)",
        "MathF.Exp(fx)",
        "MathF.Log(logF)",
        "MathF.Pow(powBaseF,powExpF)",
        "Math.Clamp(fx,-1,1)",
    ];

    /// <summary>The <see cref="double"/> columns of <see cref="TranscendentalSpecialValuesKernel"/> (Case N02).</summary>
    public static readonly string[] TranscendentalSpecialDoubleColumns =
    [
        "Math.Sin(dx)",
        "Math.Cos(dx)",
        "Math.Tan(dx)",
        "Math.Exp(dx)",
        "Math.Log(logD)",
        "Math.Pow(powBaseD,powExpD)",
    ];

    /// <summary>The columns of <see cref="BitOperationsMatrixKernel"/> (Case N03).</summary>
    public static readonly string[] BitOperationsColumns =
    [
        "BitOperations.LeadingZeroCount(u32)",
        "BitOperations.LeadingZeroCount(u64)",
        "BitOperations.TrailingZeroCount(u32)",
        "BitOperations.TrailingZeroCount(u64)",
        "BitOperations.PopCount(u32)",
        "BitOperations.PopCount(u64)",
        "BitOperations.Log2(u32)",
        "BitOperations.Log2(u64)",
        "BitOperations.RotateLeft(u32,s)",
        "BitOperations.RotateLeft(u64,s)",
        "BitOperations.RotateRight(u32,s)",
        "BitOperations.RotateRight(u64,s)",
        "BitOperations.RotateLeft(offset:s,value:u32)",
    ];

    /// <summary>The comparison columns of <see cref="FloatComparisonAndLargeTieMatrixKernel"/> (Case N05).</summary>
    public static readonly string[] FloatComparisonColumns =
    [
        "x==y",
        "x!=y",
        "x<y",
        "x<=y",
        "x>y",
        "x>=y",
        "!(x<y)",
        "u==v",
        "u!=v",
        "u<v",
        "u<=v",
        "u>v",
        "u>=v",
        "!(u<v)",
        "x<iv",
        "u==iv",
    ];

    /// <summary>The rounding columns of <see cref="FloatComparisonAndLargeTieMatrixKernel"/> (Case N05).</summary>
    public static readonly string[] LargeTieRoundColumns =
    [
        "MathF.Round(x)",
        "Math.Round(u)",
    ];

    /// <summary>The columns of <see cref="SingleTranscendentalMatrixKernel"/> (Case N07).</summary>
    public static readonly string[] SingleTranscendentalColumns =
    [
        "MathF.Sin(x)",
        "MathF.Cos(x)",
        "MathF.Tan(x)",
        "MathF.Exp(x)",
        "MathF.Log(x)",
        "MathF.Pow(x,y)",
    ];

    /// <summary>The columns of <see cref="DoubleAndPromotedSingleTranscendentalMatrixKernel"/> (Case N08).</summary>
    public static readonly string[] DoubleAndPromotedSingleTranscendentalColumns =
    [
        "Math.Sin(d)",
        "Math.Cos(d)",
        "Math.Tan(d)",
        "Math.Exp(d)",
        "Math.Log(d)",
        "Math.Pow(d1,d2)",
        "Math.Sin(f)",
        "Math.Cos(f)",
        "Math.Tan(f)",
        "Math.Exp(f)",
        "Math.Log(|f|+0.25f)",
        "Math.Pow(|f|+0.25f,fExp)",
    ];

    /// <summary>The columns of <see cref="FastMathMatrixKernel"/> (Case N09).</summary>
    public static readonly string[] FastMathColumns =
    [
        "FastMath.Sqrt(|x|)",
        "FastMath.Abs(x)",
        "FastMath.Exp(expX)",
        "FastMath.Log(logUnitX)",
        "FastMath.Log(logWideX)",
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

    // Case N01: Subnormal IEEE 754 0-ulp arithmetic and Sqrt without FTZ
    [GpuKernel]
    public static void SubnormalIeeeExactMatrixKernel(
        float[] fa, float[] fb, double[] da, double[] db, float[] rf, double[] rd)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= fa.Length)
        {
            return;
        }

        int of = t * 6;
        float x = fa[t];
        float y = fb[t];
        rf[of] = x + y;
        rf[of + 1] = x - y;
        rf[of + 2] = x * y;
        rf[of + 3] = x / y;
        rf[of + 4] = MathF.Sqrt(MathF.Abs(x));
        rf[of + 5] = FastMath.Sqrt(MathF.Abs(x));

        int od = t * 5;
        double u = da[t];
        double v = db[t];
        rd[od] = u + v;
        rd[od + 1] = u - v;
        rd[od + 2] = u * v;
        rd[od + 3] = u / v;
        rd[od + 4] = Math.Sqrt(Math.Abs(u));
    }

    // Case N02: Transcendental and Pow special values / fixed points (Bitwise)
    [GpuKernel]
    public static void TranscendentalSpecialValuesKernel(
        float[] fx,
        float[] logF,
        float[] powBaseF,
        float[] powExpF,
        double[] dx,
        double[] logD,
        double[] powBaseD,
        double[] powExpD,
        float[] rf,
        double[] rd
    )
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= fx.Length)
        {
            return;
        }

        int of = t * 7;
        rf[of] = MathF.Sin(fx[t]);
        rf[of + 1] = MathF.Cos(fx[t]);
        rf[of + 2] = MathF.Tan(fx[t]);
        rf[of + 3] = MathF.Exp(fx[t]);
        rf[of + 4] = MathF.Log(logF[t]);
        rf[of + 5] = MathF.Pow(powBaseF[t], powExpF[t]);
        rf[of + 6] = Math.Clamp(fx[t], -1.0f, 1.0f);

        int od = t * 6;
        rd[od] = Math.Sin(dx[t]);
        rd[od + 1] = Math.Cos(dx[t]);
        rd[od + 2] = Math.Tan(dx[t]);
        rd[od + 3] = Math.Exp(dx[t]);
        rd[od + 4] = Math.Log(logD[t]);
        rd[od + 5] = Math.Pow(powBaseD[t], powExpD[t]);
    }

    // Case N03: System.Numerics.BitOperations uint/ulong mapped overloads + shift count masking + named arguments
    // (nuint overloads emit undeclared System_UIntPtr in C++ [ND7-01] and are verified via in-memory generator test)
    [GpuKernel]
    public static void BitOperationsMatrixKernel(uint[] u32, ulong[] u64, int[] offsets, ulong[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= u32.Length)
        {
            return;
        }

        uint v32 = u32[t];
        ulong v64 = u64[t];
        int s = offsets[t];
        int o = t * 13;

        r[o] = (ulong)BitOperations.LeadingZeroCount(v32);
        r[o + 1] = (ulong)BitOperations.LeadingZeroCount(v64);
        r[o + 2] = (ulong)BitOperations.TrailingZeroCount(v32);
        r[o + 3] = (ulong)BitOperations.TrailingZeroCount(v64);
        r[o + 4] = (ulong)BitOperations.PopCount(v32);
        r[o + 5] = (ulong)BitOperations.PopCount(v64);
        r[o + 6] = (ulong)BitOperations.Log2(v32);
        r[o + 7] = (ulong)BitOperations.Log2(v64);
        r[o + 8] = BitOperations.RotateLeft(v32, s);
        r[o + 9] = BitOperations.RotateLeft(v64, s);
        r[o + 10] = BitOperations.RotateRight(v32, s);
        r[o + 11] = BitOperations.RotateRight(v64, s);
        r[o + 12] = BitOperations.RotateLeft(offset: s, value: v32);
    }

    // Case N05: IEEE 754 unordered comparisons (NaN, signed zeros, subnormals, int promotion) and large-exponent Round ties
    [GpuKernel]
    public static void FloatComparisonAndLargeTieMatrixKernel(
        float[] fa, float[] fb, double[] da, double[] db, int[] ia, byte[] rCmp, double[] rRound)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= fa.Length)
        {
            return;
        }

        float x = fa[t];
        float y = fb[t];
        double u = da[t];
        double v = db[t];
        int iv = ia[t];
        int oc = t * 16;

        rCmp[oc] = (byte)(x == y ? 1 : 0);
        rCmp[oc + 1] = (byte)(x != y ? 1 : 0);
        rCmp[oc + 2] = (byte)(x < y ? 1 : 0);
        rCmp[oc + 3] = (byte)(x <= y ? 1 : 0);
        rCmp[oc + 4] = (byte)(x > y ? 1 : 0);
        rCmp[oc + 5] = (byte)(x >= y ? 1 : 0);
        rCmp[oc + 6] = (byte)(!(x < y) ? 1 : 0);
        rCmp[oc + 7] = (byte)(u == v ? 1 : 0);
        rCmp[oc + 8] = (byte)(u != v ? 1 : 0);
        rCmp[oc + 9] = (byte)(u < v ? 1 : 0);
        rCmp[oc + 10] = (byte)(u <= v ? 1 : 0);
        rCmp[oc + 11] = (byte)(u > v ? 1 : 0);
        rCmp[oc + 12] = (byte)(u >= v ? 1 : 0);
        rCmp[oc + 13] = (byte)(!(u < v) ? 1 : 0);
        rCmp[oc + 14] = (byte)(x < iv ? 1 : 0);
        rCmp[oc + 15] = (byte)(u == iv ? 1 : 0);

        int or = t * 2;
        rRound[or] = MathF.Round(x);
        rRound[or + 1] = Math.Round(u);
    }

    // Case N06: Mul-Add exact inputs (Bitwise) and FMA contraction witness (Verify)
    [GpuKernel]
    public static void MulAddExactAndContractionKernel(
        float[] fa, float[] fb, float[] fc, float[] rExact, float[] rContract)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= fa.Length)
        {
            return;
        }

        rExact[t] = fa[t] * fb[t] + fc[t];
        float k = fa[t];
        rContract[t] = k * k - (1.0f + 2.0f * 0.000244140625f);
    }

    // Case N07: MathF single-precision transcendentals (per-column CUDA ULP + Verify mathematical identities)
    [GpuKernel]
    public static void SingleTranscendentalMatrixKernel(
        float[] trigX, float[] expX, float[] logX, float[] powBase, float[] powExp, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= trigX.Length)
        {
            return;
        }

        int o = t * 6;
        r[o] = MathF.Sin(trigX[t]);
        r[o + 1] = MathF.Cos(trigX[t]);
        r[o + 2] = MathF.Tan(trigX[t]);
        r[o + 3] = MathF.Exp(expX[t]);
        r[o + 4] = MathF.Log(logX[t]);
        r[o + 5] = MathF.Pow(powBase[t], powExp[t]);
    }

    // Case N08: System.Math double-precision transcendentals + float argument widening (#876)
    [GpuKernel]
    public static void DoubleAndPromotedSingleTranscendentalMatrixKernel(
        double[] trigD, double[] expD, double[] logD, double[] powBaseD, double[] powExpD,
        float[] fArg, float[] fExp, double[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= trigD.Length)
        {
            return;
        }

        int o = t * 12;
        r[o] = Math.Sin(trigD[t]);
        r[o + 1] = Math.Cos(trigD[t]);
        r[o + 2] = Math.Tan(trigD[t]);
        r[o + 3] = Math.Exp(expD[t]);
        r[o + 4] = Math.Log(logD[t]);
        r[o + 5] = Math.Pow(powBaseD[t], powExpD[t]);
        r[o + 6] = Math.Sin(fArg[t]);
        r[o + 7] = Math.Cos(fArg[t]);
        r[o + 8] = Math.Tan(fArg[t]);
        r[o + 9] = Math.Exp(fArg[t]);
        r[o + 10] = Math.Log(MathF.Abs(fArg[t]) + 0.25f);
        r[o + 11] = Math.Pow(MathF.Abs(fArg[t]) + 0.25f, fExp[t]);
    }

    // Case N09: FastMath (Sqrt, Abs, Exp, Log)
    [GpuKernel]
    public static void FastMathMatrixKernel(
        float[] x, float[] expX, float[] logUnitX, float[] logWideX, float[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= x.Length)
        {
            return;
        }

        int o = t * 5;
        r[o] = FastMath.Sqrt(MathF.Abs(x[t]));
        r[o + 1] = FastMath.Abs(x[t]);
        r[o + 2] = FastMath.Exp(expX[t]);
        r[o + 3] = FastMath.Log(logUnitX[t]);
        r[o + 4] = FastMath.Log(logWideX[t]);
    }
}
