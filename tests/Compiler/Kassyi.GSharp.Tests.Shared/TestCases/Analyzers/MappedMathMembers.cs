using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// #899: the members of Math and MathF that the dialects map (StandardCpp.yml, inherited by GSharpCuda.yml), and the
// constants, are not reported
public static partial class MappedMathMembers
{
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer input")]
    public static void MappedMathKernel(double[] d, float[] f, int[] i)
    {
        int t = ThreadIndex.X;
        d[t] = Math.Abs(d[t]) + Math.Max(d[t], 1.0) + Math.Min(d[t], 2.0) + Math.Sqrt(d[t]) + Math.Pow(d[t], 2.0) +
               Math.Floor(d[t]) + Math.Clamp(d[t], 0.0, 1.0) + Math.PI + Math.E;
        f[t] = MathF.Abs(f[t]) + MathF.Max(f[t], 1f) + MathF.Min(f[t], 2f) + MathF.Sqrt(f[t]) + MathF.Sin(f[t]) +
               Math.Abs(f[t]) + Math.Max(f[t], 1f) + MathF.PI;
        i[t] = Math.Abs(i[t]) + Math.Max(i[t], 1) + Math.Min(i[t], 2) + Math.Clamp(i[t], 0, 9);
    }
}
