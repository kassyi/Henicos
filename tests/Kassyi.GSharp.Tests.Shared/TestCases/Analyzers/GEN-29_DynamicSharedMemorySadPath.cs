using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static partial class GEN29_DynamicSharedMemorySadPathCase
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void DynamicSharedMemoryKernel(int dynamicSize, int[] result)
    {
        // Sad Path: SharedMemory<T> requires compile-time constant size (or static array size)
        SharedMemory<int> smem = new(dynamicSize);
        result[0] = smem[0];
    }
}
