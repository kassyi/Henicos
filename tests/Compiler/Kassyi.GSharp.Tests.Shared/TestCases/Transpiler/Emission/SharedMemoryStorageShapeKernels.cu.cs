#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Storage declarations of SharedMemory<T> checked by SharedMemoryStorageTranspilerTests")]
public static partial class SharedMemoryStorageShapeKernels
{
    [GpuDeviceFunction]
    public static void PickShared(SharedMemory<int> a, out SharedMemory<int> result)
    {
        result = a;
    }

    // An out variable declared after an early return of a Gpu.SyncGrid body is declared before the body
    [GpuKernel(Cooperative = true)]
    public static void SyncGridOutVarKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            if (tid >= 8)
            {
                return;
            }

            PickShared(new SharedMemory<int>(8), out var o);
            o[tid] = tid + 1;
            output[tid] = o[tid];
        });
    }
}
