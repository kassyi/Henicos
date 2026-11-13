using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: a SharedMemory<T> that is created without an element count. The language cannot block `default`, and a
// shared memory declaration of unknown size has no meaning on any path (#643)
public static partial class SharedMemoryWithoutCountKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DefaultLiteralKernel(int[] output)
    {
        SharedMemory<int> s = default;
        output[0] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DefaultExpressionKernel(int[] output)
    {
        var s = default(SharedMemory<float>);
        output[0] = 1;
    }

    // Held across a barrier, the declaration is part of the CPU state machine's context
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DefaultAcrossBarrierKernel(int[] output)
    {
        SharedMemory<int> s = default;
        Gpu.SyncThreads();
        output[ThreadIndex.X] = 1;
    }

    [GpuDeviceFunction]
    public static SharedMemory<int> DefaultReturn() => default;

    // Not reported: a declaration with a count, and a default of another type
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AcceptedKernel(int[] output)
    {
        var s = new SharedMemory<int>(4);
        int zero = default;
        s[ThreadIndex.X % 4] = zero;
        output[0] = s[0];
    }
}
