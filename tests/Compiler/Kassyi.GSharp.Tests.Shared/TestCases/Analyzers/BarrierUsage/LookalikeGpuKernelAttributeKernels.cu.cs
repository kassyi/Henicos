using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class LookalikeGpuKernelAttributeKernels
{
    // A user's own attribute that only shares its name with Kassyi.GSharp.Core.GpuKernelAttribute
    public static class Lookalike
    {
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class GpuKernelAttribute : Attribute;
    }

    // Host code, not a kernel: GSH0042 instead of GSH0037
    [Lookalike.GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void NotAKernel(int[] buffer)
    {
        if (ThreadIndex.X < 16)
        {
            Gpu.SyncThreads();
        }
    }
}
