using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class SadPath_GSH0038_Exception
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void TryCatchKernel(int[] buffer)
    {
        try
        {
            buffer[0] = 1;
            Gpu.SyncThreads();
        }
        catch (Exception)
        {
            buffer[0] = -1;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void TryFinallyKernel(int[] buffer)
    {
        try
        {
            buffer[0] = 10;
        }
        finally
        {
            buffer[0] = 20;
        }
    }
}
