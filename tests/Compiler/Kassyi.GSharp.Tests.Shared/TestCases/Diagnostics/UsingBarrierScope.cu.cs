using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct BarrierScopeResource : IDisposable
{
    public int[] Log;

    public void Dispose() => Log[0] = 1;
}

public sealed class ClassScopeResource : IDisposable
{
    public void Dispose()
    {
    }
}

// GSH0003 for a barrier in the scope of a using: in a using statement, after a using declaration of the same block, and
// anywhere in a nested block that has a using declaration
public static partial class UsingBarrierScopeKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarrierInUsingKernel(int[] log)
    {
        using (var statement = new BarrierScopeResource { Log = log })
        {
            Gpu.SyncThreads();
        }

        using var declaration = new BarrierScopeResource { Log = log };
        Gpu.SyncThreads();
    }

    [GpuKernel]
    public static void BarrierInNestedBlockKernel(int[] log)
    {
        {
            Gpu.SyncThreads();
            using var declaration = new BarrierScopeResource { Log = log };
        }
    }

    // Not reported: the using statement ends before the barrier, and the declaration comes after it in the body
    [GpuKernel]
    public static void BarrierOutsideUsingKernel(int[] log)
    {
        using (var statement = new BarrierScopeResource { Log = log })
        {
            log[1] = 1;
        }

        Gpu.SyncThreads();
        using var declaration = new BarrierScopeResource { Log = log };
        log[2] = 2;
    }

    // A class cannot be created in device code (GSH0001), and the using has no struct to dispose (GSH0003)
    [GpuKernel]
    public static void ClassResourceKernel(int[] log)
    {
        using (var resource = new ClassScopeResource())
        {
            log[0] = 1;
        }
    }
}
