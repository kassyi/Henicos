using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.SharedMemoryDeclaration;

public static partial class SharedMemorySameLineOverloadKernels
{
    // GSH0063: the overloads share the method name, so under managed execution their declarations share one buffer
    [GpuDeviceFunction] public static int Scratch(int value) { var box = new SharedMemory<int>(32); box[ThreadIndex.X] = value; return box[ThreadIndex.X]; } [GpuDeviceFunction] public static float Scratch(float value) { var box = new SharedMemory<float>(32); box[ThreadIndex.X] = value; return box[ThreadIndex.X]; }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SameLineOverloadKernel(int[] buffer, float[] values)
    {
        buffer[ThreadIndex.X] = Scratch(buffer[ThreadIndex.X]);
        values[ThreadIndex.X] = Scratch(values[ThreadIndex.X]);
    }
}
