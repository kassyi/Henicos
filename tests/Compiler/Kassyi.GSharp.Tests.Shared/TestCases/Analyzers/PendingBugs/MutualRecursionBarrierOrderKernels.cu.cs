#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.PendingBugs;

// Mutual recursion cycle CycleA <-> CycleB where CycleB reaches Gpu.SyncThreads().
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class MutualRecursionBarrierOrderKernels
{
    [GpuDeviceFunction]
    public static int CycleA(int n)
    {
        if (n <= 0)
        {
            return 0;
        }

        return CycleB(n - 1);
    }

    [GpuDeviceFunction]
    public static int CycleB(int n)
    {
        Gpu.SyncThreads();
        if (n <= 0)
        {
            return 1;
        }

        return CycleA(n - 1);
    }

    [GpuDeviceFunction]
    public static int EntryToA(int n) => CycleA(n);

    [GpuDeviceFunction]
    public static int EntryToB(int n) => CycleB(n);
}
