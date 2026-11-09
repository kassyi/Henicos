using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static partial class SplitAfterLeftOperandKernels
{
    private const uint FullMask = 0xFFFFFFFFu;

    [GpuDeviceFunction]
    public static int Bump(ref Span<int> s)
    {
        s = s.Slice(1);
        return 1;
    }

    [GpuDeviceFunction]
    public static int Broadcast(int value)
    {
        return Warp.ShuffleSync(FullMask, value, 0);
    }

    // The left operand changes s and runs before the split, as C# evaluates it; s is read after the split (GSH0039)
    [GpuDeviceFunction]
    public static int ChangedInLeftOperand(Span<int> s, bool c)
    {
        int v = (c ? Bump(ref s) : 0) + Broadcast(0);
        return v + s[0];
    }

    // The left operand reads s before the split and nothing reads it after: not live across it
    [GpuDeviceFunction]
    public static int ReadInLeftOperandOnly(Span<int> s)
    {
        s = s.Slice(1);
        return s[0] + Broadcast(0);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SplitAfterLeftOperandKernel(int[] input, int[] result)
    {
        result[ThreadIndex.X] = ChangedInLeftOperand(input, true) + ReadInLeftOperandOnly(input);
    }
}
