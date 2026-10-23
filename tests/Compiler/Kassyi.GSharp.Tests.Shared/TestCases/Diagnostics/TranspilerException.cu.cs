using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// The transpiler throws on this input: the getter of a computed property is transpiled with the struct (#471), and a
// SharedMemory<T> created there is not inside a method, so SharedMemorySiteKey has no declaration to key it by
public struct SharedMemoryInGetter
{
    public int A;

    public int Size => new SharedMemory<int>(4)[0] + A;
}

public static partial class TranspilerExceptionKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ReadGetterKernel(int[] data, SharedMemoryInGetter value)
    {
        data[0] = value.Size;
    }
}
