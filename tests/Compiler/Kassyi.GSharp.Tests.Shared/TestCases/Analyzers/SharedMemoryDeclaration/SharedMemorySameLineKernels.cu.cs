using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.SharedMemoryDeclaration;

public static partial class SharedMemorySameLineKernels
{
    [GpuDeviceFunction]
    public static int SumPair(int value)
    {
        // GSH0063: under managed execution both declarations get the key of this line and share one buffer
        var left = new SharedMemory<int>(32); var right = new SharedMemory<int>(32);
        left[ThreadIndex.X] = value;
        right[ThreadIndex.X] = value * 2;
        return left[ThreadIndex.X] + right[ThreadIndex.X];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SameLineKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = SumPair(buffer[ThreadIndex.X]);
    }
}
