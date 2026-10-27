using System;
using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Core.Gpu;
using G = Kassyi.GSharp.Core.Gpu;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A suspending call in the body of Gpu.ForUnroll is inlined, however Gpu.ForUnroll is spelled (no GSH0062)
public static partial class MacroSpellingSuspendingCallKernels
{
    [GpuDeviceFunction]
    public static int Broadcast(int value) => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);

    [GpuKernel]
    [SkipSnapshotConsistency("Analyzer case")]
    public static void SpelledForUnrollKernel(int[] buffer)
    {
        Kassyi.GSharp.Core.Gpu.ForUnroll(0, 2, i => { buffer[ThreadIndex.X] = Broadcast(buffer[ThreadIndex.X] + i); });
        G.ForUnroll(0, 2, i => { buffer[ThreadIndex.X] = Broadcast(buffer[ThreadIndex.X] + i); });
        ForUnroll(0, 2, i => { buffer[ThreadIndex.X] = Broadcast(buffer[ThreadIndex.X] + i); });
    }
}

// A user's own Gpu.ForUnroll is an ordinary method taking a lambda: a suspending call in it is GSH0062
public static partial class LookalikeMacroSuspendingCallKernels
{
    public static class Gpu
    {
        public static void ForUnroll(int start, int count, Action<int> body)
        {
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LookalikeForUnrollKernel(int[] buffer)
    {
        Gpu.ForUnroll(0, 2, i => { buffer[ThreadIndex.X] = MacroSpellingSuspendingCallKernels.Broadcast(i); });
    }
}
