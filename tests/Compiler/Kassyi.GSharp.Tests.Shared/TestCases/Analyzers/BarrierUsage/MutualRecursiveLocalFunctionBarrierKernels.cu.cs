using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class MutualRecursiveLocalFunctionBarrierKernels
{
    // GSH0045: two local functions that call each other, one of them with a barrier
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void MutualRecursiveLocalFunctionBarrierKernel(int depth)
    {
        void Ping(int d)
        {
            if (d <= 0) return;

            Gpu.SyncThreads();
            Pong(d - 1);
        }

        void Pong(int d) => Ping(d);

        Ping(depth);
    }

    // GSH0045: a local function with a barrier that calls itself through a method group
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void MethodGroupRecursiveLocalFunctionBarrierKernel(int depth)
    {
        void Step(int d)
        {
            if (d <= 0) return;

            Gpu.SyncThreads();
            Apply(Step, d - 1);
        }

        static void Apply(System.Action<int> f, int d) => f(d);

        Step(depth);
    }
}
