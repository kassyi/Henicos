using System;
using Kassyi.GSharp.Core;
using static System.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// #899: a member of Math or MathF that no dialect maps, or an overload of a mapped name that is not mapped, is reported:
// called with its type, through using static System.Math, or converted to a delegate; a mapped member is reported as
// a method group, which has no native function to convert
public static partial class UnmappedMathMembers
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UnmappedMathKernel(long[] l, float[] f, double[] d)
    {
        int t = ThreadIndex.X;
        l[t] = Math.Abs(l[t]) + Math.Max(l[t], 1L);
        f[t] = MathF.Cbrt(f[t]);
        Func<long, long> g = Math.Abs;
        d[t] = Cbrt(d[t]);
        l[t] = Abs(l[t]);
        Func<int, int> g2 = Math.Abs;
        Func<int, int> g3 = Abs;
        Func<long, long> g1 = Abs;
    }
}
