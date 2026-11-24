using System;
using Kassyi.GSharp.Core;

namespace StackBodyScopedCases;

// A scoped span declared without a value in a kernel that reaches Warp.ActiveMask(): the body lowered for the stacks
// gives it default(Span<uint>), without scoped, which belongs to the local and not to its type
[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class ScopedKernels
{
    [GpuKernel]
    public static void ScopedSpanKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        scoped Span<uint> s;
        s = stackalloc uint[2];
        s[0] = Warp.ActiveMask();
        s[1] = (uint)tid;
        result[tid] = s[0] ^ s[1];
    }
}
