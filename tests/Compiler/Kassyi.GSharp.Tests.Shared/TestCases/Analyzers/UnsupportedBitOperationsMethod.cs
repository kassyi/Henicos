using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_155_UnsupportedBitOperationsMethod
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithUnsupportedBitOperations(uint[] data)
    {
        // Calling an unmapped BitOperations method (e.g. BitOperations.Crc32C) in kernel
        // triggers unmapped method diagnostic GSH0109
        uint result = BitOperations.Crc32C(data[0], 1u);
        data[0] = result;
    }
}
