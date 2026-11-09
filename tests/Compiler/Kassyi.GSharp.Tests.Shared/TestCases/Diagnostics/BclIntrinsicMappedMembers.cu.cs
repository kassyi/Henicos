using System;
using System.Numerics;
using System.Threading;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class BclIntrinsicMappedKernels
{
    // Mapped by [BclIntrinsicMap]: accepted
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MappedInterlockedKernel(int[] counter)
    {
        Interlocked.Add(ref counter[0], 1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MappedBitOperationsKernel(uint[] data, int[] output)
    {
        output[ThreadIndex.X] = BitOperations.PopCount(data[ThreadIndex.X]);
    }

    // Not mapped: GSH0003
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnmappedBitOperationsKernel(uint[] data)
    {
        data[0] = BitOperations.RoundUpToPowerOf2(data[0]);
    }

    // Not mapped for long and ulong operands of Or: GSH0003
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnmappedInterlockedOverloadKernel(long[] counter)
    {
        Interlocked.Or(ref counter[0], 1L);
    }

    // A mapped method used as a method group: GSH0003
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void MappedMethodGroupKernel(uint[] data, int[] output)
    {
        Func<uint, int> popCount = BitOperations.PopCount;
        output[0] = popCount(data[0]);
    }
}
