// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Managed-launch kernel exercising <c>ref</c> local and <c>ref</c> parameter writes through
/// <see cref="SharedMemory{T}"/> indexers.
/// </summary>
[SkipSnapshotConsistency("Managed ref indexer write-through check; verified by SharedMemoryRegionManagedTests")]
public static partial class SharedMemoryRegionManagedKernels
{
    [GpuKernel]
    public static void RefIndexerWriteThroughKernel(int[] output)
    {
        var smem = new SharedMemory<int>(4);
        ref int slot = ref smem[2];
        slot = 77;
        output[0] = smem[2];

        WriteByRef(ref smem[2], 123);
        output[1] = smem[2];
    }

    [GpuDeviceFunction]
    private static void WriteByRef(ref int target, int value) => target = value;
}
