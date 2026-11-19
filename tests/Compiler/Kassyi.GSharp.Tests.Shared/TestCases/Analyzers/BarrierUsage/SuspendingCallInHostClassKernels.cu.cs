using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SuspendingCallInHostClassKernels
{
    [GpuDeviceFunction]
    public static int TaggedRotate(int tag)
    {
        SharedMemory<int> shared = new(256);
        int tid = ThreadIndex.X;
        shared[tid] = tag + tid;
        Gpu.SyncThreads();
        return shared[tid ^ 1];
    }

    // Guard: reference-type (class) members are host code, never lowered to GPU state machines
    public sealed class HostHolder
    {
        public int Value;
        public int Field = TaggedRotate(2);
        public int Init { get; init; } = TaggedRotate(1);

        public HostHolder(int tag)
        {
            Value = TaggedRotate(tag);
        }

        public int Rotated => TaggedRotate(Value);

        public static implicit operator int(HostHolder holder) => TaggedRotate(holder.Value);
    }
}