using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_155_UnsupportedBitOperationsMethod
{
    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithUnsupportedBitOperations(uint[] data)
    {
        // Calling an unmapped BitOperations method (e.g. BitOperations.RoundUpToPowerOf2) in kernel
        // triggers unsupported syntax diagnostic GSH0003
        uint result = BitOperations.RoundUpToPowerOf2(data[0]);
        data[0] = result;
    }
}
