using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// The shapes that are accepted are compiled and run in Syntax/OutSpanParameterKernels.cu.cs; this case holds the one
// that stays rejected
public static partial class OutSpanOverwriteKernels
{
    [GpuDeviceFunction]
    public static void Fill(int[] buffer, out Span<int> s)
    {
        s = buffer;
    }

    // Read after the barrier before it is overwritten: live across it (GSH0039)
    [GpuDeviceFunction]
    public static int ReadBeforeOverwrite(int[] buffer, Span<int> s)
    {
        s = s.Slice(1);
        Gpu.SyncThreads();
        int first = s[0];
        Fill(buffer, out s);
        return first + s[0];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void OutSpanOverwriteKernel(int[] input, int[] buffer, int[] result)
    {
        result[ThreadIndex.X] = ReadBeforeOverwrite(buffer, input);
    }
}
