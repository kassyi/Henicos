using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// #899: every member of Math and MathF that StandardCpp.yml maps (inherited by GSharpCuda.yml), each overload called
// once, and the constants, are not reported. A YAML signature that no longer matches the symbol shows up here.
public static partial class MappedMathMembers
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer input")]
    public static void MappedMathKernel(double[] d, float[] f, int[] i)
    {
        int t = ThreadIndex.X;
        // Math, double
        d[t] = Math.Abs(d[t]);
        d[t] = Math.Max(d[t], 1.0);
        d[t] = Math.Min(d[t], 2.0);
        d[t] = Math.Clamp(d[t], 0.0, 1.0);
        d[t] = Math.Sin(d[t]);
        d[t] = Math.Cos(d[t]);
        d[t] = Math.Tan(d[t]);
        d[t] = Math.Exp(d[t]);
        d[t] = Math.Sqrt(d[t]);
        d[t] = Math.Log(d[t]);
        d[t] = Math.Floor(d[t]);
        d[t] = Math.Ceiling(d[t]);
        d[t] = Math.Pow(d[t], 2.0);
        d[t] = Math.Round(d[t]);
        // Math, float
        f[t] = Math.Abs(f[t]);
        f[t] = Math.Max(f[t], 1f);
        f[t] = Math.Min(f[t], 2f);
        f[t] = Math.Clamp(f[t], 0f, 1f);
        // Math, int
        i[t] = Math.Abs(i[t]);
        i[t] = Math.Max(i[t], 1);
        i[t] = Math.Min(i[t], 2);
        i[t] = Math.Clamp(i[t], 0, 9);
        // MathF
        f[t] = MathF.Abs(f[t]);
        f[t] = MathF.Max(f[t], 1f);
        f[t] = MathF.Min(f[t], 2f);
        f[t] = MathF.Sin(f[t]);
        f[t] = MathF.Cos(f[t]);
        f[t] = MathF.Tan(f[t]);
        f[t] = MathF.Exp(f[t]);
        f[t] = MathF.Sqrt(f[t]);
        f[t] = MathF.Log(f[t]);
        f[t] = MathF.Floor(f[t]);
        f[t] = MathF.Ceiling(f[t]);
        f[t] = MathF.Pow(f[t], 2f);
        f[t] = MathF.Round(f[t]);
        // Constants
        d[t] += Math.PI + Math.E;
        f[t] += MathF.PI + MathF.E;
    }
}
