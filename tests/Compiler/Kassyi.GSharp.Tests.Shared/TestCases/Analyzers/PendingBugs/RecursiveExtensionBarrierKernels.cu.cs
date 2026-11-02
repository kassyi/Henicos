#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.PendingBugs;

// #926: Non-generic extension [GpuDeviceFunction] reaching Gpu.SyncThreads() via reduced extension syntax
// ((x - 1).DirectRec() / (x - 1).MutualA()).
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class RecursiveExtensionBarrierKernels
{
    [GpuDeviceFunction]
    public static void DirectRec(this int x)
    {
        Gpu.SyncThreads();
        if (x > 0)
        {
            (x - 1).DirectRec();
        }
    }

    [GpuDeviceFunction]
    public static void MutualA(this int x)
    {
        if (x > 0)
        {
            (x - 1).MutualB();
        }
    }

    [GpuDeviceFunction]
    public static void MutualB(this int x)
    {
        Gpu.SyncThreads();
        if (x > 0)
        {
            (x - 1).MutualA();
        }
    }

    [GpuKernel]
    public static void RunKernel(int[] output)
    {
        int seed = output[0];
        seed.DirectRec();
        seed.MutualA();
    }
}
