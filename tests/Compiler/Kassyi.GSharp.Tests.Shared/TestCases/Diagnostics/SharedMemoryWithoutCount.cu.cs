using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: a SharedMemory<T> that is created without an element count. The language cannot block `default`, and a
// shared memory declaration of unknown size has no meaning on any path (#643)
public ref struct SharedMemoryHolder
{
    public SharedMemory<int> Memory;
}

public ref struct SharedMemoryPropertyHolder
{
    public SharedMemory<int> Memory { get; set; }
}

public static partial class SharedMemoryWithoutCountKernels
{
    [GpuDeviceFunction]
    public static int Use(SharedMemory<int> memory) => 1;

    [GpuDeviceFunction]
    public static T Make<T>() where T : allows ref struct => default;

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

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConditionalKernel(int[] output, bool flag)
    {
        var s = flag ? new SharedMemory<int>(4) : default;
        output[0] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SwitchArmKernel(int[] output, int mode)
    {
        var s = mode switch
        {
            0 => new SharedMemory<int>(4),
            _ => default
        };
        output[0] = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ArgumentKernel(int[] output)
    {
        output[0] = Use(default);
    }

    [GpuDeviceFunction]
    public static int ParameterDefault(SharedMemory<int> memory = default) => 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NoInitializerKernel(int[] output)
    {
        SharedMemory<int> s;
        s = new SharedMemory<int>(4);
        output[0] = s[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HolderDefaultKernel(int[] output)
    {
        SharedMemoryHolder holder = default;
        output[0] = holder.Memory[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HolderNewKernel(int[] output)
    {
        var holder = new SharedMemoryHolder();
        output[0] = holder.Memory[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void HolderPropertyKernel(int[] output)
    {
        SharedMemoryPropertyHolder holder = default;
        output[0] = holder.Memory[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GenericDefaultKernel(int[] output)
    {
        var s = Make<SharedMemory<int>>();
        output[0] = 1;
    }

    // Not reported: a declaration with a count, the default of another type, a holder whose memory is assigned (also by
    // an object initializer) and a holder that a call may fill in
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AcceptedKernel(int[] output)
    {
        var s = new SharedMemory<int>(4);
        int zero = default;
        s[ThreadIndex.X % 4] = zero;

        SharedMemoryHolder assigned = default;
        assigned.Memory = s;
        var initialized = new SharedMemoryHolder { Memory = s };
        SharedMemoryHolder replaced = default;
        replaced = initialized;

        output[0] = s[0] + assigned.Memory[0] + initialized.Memory[0] + replaced.Memory[0];
    }
}
