using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// #899: a member of Math or MathF that no dialect maps, or an overload of a mapped name that is not mapped, is reported
public static partial class UnmappedMathMembers
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UnmappedMathKernel(long[] l, float[] f)
    {
        int t = ThreadIndex.X;
        l[t] = Math.Abs(l[t]) + Math.Max(l[t], 1L);
        f[t] = MathF.Cbrt(f[t]);
    }
}
