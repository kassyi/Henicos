using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.SharedMemoryDeclaration;

public static partial class SharedMemoryDistinctKeyKernels
{
    // Methods with different names may share a line: the method name is part of the key
    [GpuDeviceFunction] public static int First(int value) { var a = new SharedMemory<int>(32); a[ThreadIndex.X] = value; return a[ThreadIndex.X]; } [GpuDeviceFunction] public static int Second(int value) { var b = new SharedMemory<int>(32); b[ThreadIndex.X] = value; return b[ThreadIndex.X]; }

    [GpuDeviceFunction]
    public static int SumPair(int value)
    {
        var left = new SharedMemory<int>(32);
        var right = new SharedMemory<int>(32);
        left[ThreadIndex.X] = value;
        right[ThreadIndex.X] = value * 2;
        return left[ThreadIndex.X] + right[ThreadIndex.X];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer-only case")]
    public static void DistinctKeyKernel(int[] buffer)
    {
        buffer[ThreadIndex.X] = First(buffer[ThreadIndex.X]) + Second(buffer[ThreadIndex.X]) + SumPair(buffer[ThreadIndex.X]);
    }
}
