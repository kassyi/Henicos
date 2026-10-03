using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SadPath_GSH0041_LocalPointer
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public unsafe static void CrossBarrierStackallocKernel()
    {
        int* localArr = stackalloc int[10];
        localArr[0] = 1;

        // GSH0041: stackalloc pointer cannot cross barrier
        Gpu.SyncThreads();

        localArr[0] = 2;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public unsafe static void CrossBarrierLocalAddressKernel()
    {
        int localVal = 42;
        int* ptr = &localVal;

        // GSH0041: local address pointer cannot cross barrier
        Gpu.SyncThreads();

        *ptr = 100;
    }
}
