using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public struct CollideRes : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Mode;

    public void Dispose()
    {
        Log[Slot] = Log[Slot] + 1;
#pragma warning disable CS8509
        Log[Slot + 8] = Mode switch { 0 => 1 };
#pragma warning restore CS8509
    }
}

[SkipSnapshotConsistency]
public static partial class SharedMemberErrorSiteRuntimeKernels
{
    [GpuKernel]
    public static void FirstKernel(int[] log, int[] outs, int mode)
    {
        using (var b = new CollideRes { Log = log, Slot = 1, Mode = mode })
        {
            outs[0] = 1;
        }
    }

    [GpuKernel]
    public static void SecondKernel(int[] log, int[] outs, int mode)
    {
        using (var b = new CollideRes { Log = log, Slot = 2, Mode = mode })
        {
            outs[0] = 2;
        }
    }
}
