// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

public static partial class MathClampInvertedBoundsKernels
{
    private const int High = 10;
    private const double Half = 0.5;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void InvertedBoundsKernel(int[] v, long[] l, byte[] b, double[] d)
    {
        int t = ThreadIndex.X;
        const int LocalLow = 9;
        v[t] = Math.Clamp(v[t], 10, 5);
        v[t] = Math.Clamp(v[t], High, LocalLow - 1);
        l[t] = Math.Clamp(l[t], 10L, 5L);
        b[t] = Math.Clamp(b[t], (byte)10, (byte)5);
        d[t] = Math.Clamp(d[t], 1.0, Half);
        v[t] = Math.Clamp(min: 10, max: 5, value: v[t]);
        d[t] = InvertedInDevice(d[t]);
    }

    [GpuDeviceFunction]
    private static double InvertedInDevice(double x) => Math.Clamp(x, 2.0, 1.0);

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AcceptedBoundsKernel(int[] v, double[] d, int lo)
    {
        int t = ThreadIndex.X;
        v[t] = Math.Clamp(v[t], 5, 10);
        v[t] = Math.Clamp(v[t], 5, 5);
        v[t] = Math.Clamp(v[t], lo, 5);
        d[t] = Math.Clamp(d[t], double.NaN, 0.0);
        d[t] = Math.Clamp(d[t], 0.0, -0.0);
        d[t] = Math.Clamp(d[t], 1.0, double.NaN);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void InfiniteBoundsKernel(double[] d, float[] f)
    {
        int t = ThreadIndex.X;
        d[t] = Math.Clamp(d[t], double.PositiveInfinity, double.NegativeInfinity);
        f[t] = Math.Clamp(f[t], 0f, float.NegativeInfinity);
        f[t] = Math.Clamp(f[t], 1f, 0.5f);
    }
}
