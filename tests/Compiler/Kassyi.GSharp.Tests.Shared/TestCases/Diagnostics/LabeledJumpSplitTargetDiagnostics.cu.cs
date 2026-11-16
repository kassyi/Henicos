using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Diagnostics;

[SkipSnapshotConsistency("Diagnostics case")]
public static partial class LabeledJumpSplitTargetDiagnostics
{
    [GpuDeviceFunction]
    public static void SuspendingDeviceHelper() => Gpu.SyncThreads();

    [GpuKernel]
    public static void SyncThreadsInOuterLoopKernel(int[] data)
    {
        outer:
        for (var i = 0; i < 2; i++)
        {
            Gpu.SyncThreads();
            for (var j = 0; j < 2; j++)
            {
                if (data[0] < 0)
                {
                    break outer;
                }

                if (data[0] == 0)
                {
                    continue outer;
                }
            }
        }
    }

    [GpuKernel]
    public static void ForUnrollBarrierInOuterLoopKernel(int[] data)
    {
        outer:
        for (var i = 0; i < 2; i++)
        {
            Gpu.ForUnroll(0, 2, u =>
            {
                Gpu.SyncThreads();
            });
            for (var j = 0; j < 2; j++)
            {
                if (data[0] < 0)
                {
                    break outer;
                }
            }
        }
    }

    [GpuKernel]
    public static void SyncGridBodyInOuterLoopKernel(int[] data)
    {
        outer:
        for (var i = 0; i < 2; i++)
        {
            Gpu.SyncGrid(() =>
            {
                data[0] = 1;
                data[1] = 2;
            });
            for (var j = 0; j < 2; j++)
            {
                if (data[0] < 0)
                {
                    break outer;
                }
            }
        }
    }

    [GpuKernel]
    public static void SuspendingCallInOuterLoopKernel(int[] data)
    {
        outer:
        for (var i = 0; i < 2; i++)
        {
            SuspendingDeviceHelper();
            for (var j = 0; j < 2; j++)
            {
                if (data[0] < 0)
                {
                    break outer;
                }
            }
        }
    }

    [GpuKernel]
    public static void BarrierOutsideTargetLoopKernel(int[] data)
    {
        Gpu.SyncThreads();
        outer:
        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                if (data[0] < 0)
                {
                    break outer;
                }

                if (data[0] == 0)
                {
                    continue outer;
                }
            }
        }
    }
}
