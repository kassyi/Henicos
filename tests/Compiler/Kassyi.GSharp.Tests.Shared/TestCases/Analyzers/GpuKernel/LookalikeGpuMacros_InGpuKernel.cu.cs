using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.GpuKernel;

public static partial class LookalikeGpuMacrosKernels
{
    // A user's own type that only shares its name and member names with Kassyi.GSharp.Core.Gpu
    public static class Lookalike
    {
        public static class Gpu
        {
            public static void ForUnroll(int start, int count, Action<int> body)
            {
            }

            public static void InlinePtx(string ptx)
            {
            }

            public static void SyncGrid(Action body)
            {
            }

            public static class Hint
            {
                public static void Unroll(int factor = 0)
                {
                }
            }
        }
    }

    // None of these are G# macros, so none of the macro argument rules apply
    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void UserMacrosKernel(int[] data, int count, int factor)
    {
        string ptx = "mov.u32 %0, %1;";
        Lookalike.Gpu.ForUnroll(0, count, i => data[i] = i);
        Lookalike.Gpu.InlinePtx(ptx);
        Lookalike.Gpu.SyncGrid(() => data[0] = 1);
        Lookalike.Gpu.Hint.Unroll(factor);
    }
}
