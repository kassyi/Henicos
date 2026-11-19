using System;
using Kassyi.GSharp.Core;
using static System.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// #899: a member of Math or MathF that no dialect maps, or an overload of a mapped name that is not mapped, is GSH0109:
// called with its type, through using static System.Math, or converted to a delegate; a mapped member is reported as
// a method group, which has no native function to convert
public static partial class UnmappedMathMembers
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UnmappedMathKernel(long[] l, float[] f, double[] d)
    {
        int t = ThreadIndex.X;
        d[t] = Math.Round(d[t], 1) + Math.ScaleB(d[t], 1);
        f[t] = MathF.Cbrt(f[t]);
        Func<double, int, double> g = Math.Round;
        d[t] = Cbrt(d[t]);
        d[t] = Round(d[t], 1);
        Func<int, int> g2 = Math.Abs;
        Func<int, int> g3 = Abs;
        Func<double, int, double> g1 = Round;
    }
}
