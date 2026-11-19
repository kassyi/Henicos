using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

// Two distinct kernel classes are declared so the FNV-1a class-name partitioner assigns them
// to separate translation units (with SplitUnitErrorChannelKernels landing in a non-zero unit
// whenever B >= 2), without hardcoding any unit index in the test expectations.
[SkipSnapshotConsistency("CPU split-unit runtime error location kernels; verified by runtime execution")]
public static partial class SplitUnitErrorChannelKernels
{
    [GpuKernel]
    public static void OutOfRangeElementAtKernel(int[] data, int index, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = data.AsStackEnumerable().ElementAt(index);
        }
    }

#pragma warning disable CS8509 // The switch expression intentionally omits unmatched inputs to trigger a runtime trap.
    [GpuKernel]
    public static void SwitchExpressionArmKernel(int[] keys, int[] result)
    {
        int i = ThreadIndex.X;
        result[i] = keys[i] switch { 0 => 10, 1 => 20 };
    }
#pragma warning restore CS8509

    [GpuKernel]
    public static void HealthyComputeKernel(int[] input, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var tile = new SharedMemory<int>(4);
        tile[ThreadIndex.X & 3] = input[tid];
        Gpu.SyncThreads();
        output[tid] = tile[ThreadIndex.X & 3] + 100;
    }
}

[SkipSnapshotConsistency("Companion split-unit error isolation kernels; verified by runtime execution")]
public static partial class SplitUnitCompanionKernels
{
    [GpuKernel]
    public static void HealthyCompanionKernel(int[] input, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        output[tid] = input[tid] * 3 + 1;
    }

    [GpuKernel]
    public static void CompanionElementAtTrapKernel(int[] data, int index, int[] result)
    {
        if (ThreadIndex.X == 0 && BlockIndex.X == 0)
        {
            result[0] = data.AsStackEnumerable().ElementAt(index);
        }
    }
}
