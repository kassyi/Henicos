using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class HappyPath_UniformBarriers
{
    [GpuKernel]
    public static void FlatBarrierKernel(int[] buffer)
    {
        buffer[0] = 1;
        Gpu.SyncThreads();
        buffer[0] += 2;
    }

    [GpuKernel]
    public static void LoopBarrierKernel(int[] buffer, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            buffer[i] = i;
            Gpu.SyncThreads();
        }
    }

    [GpuKernel]
    public static void SafeLocalFunctionBarrierKernel(int[] buffer)
    {
        void LocalSync()
        {
            Gpu.SyncThreads();
        }

        buffer[0] = 42;
        LocalSync();
        buffer[0] = 84;
    }

    [GpuKernel]
    public unsafe static void ScopedSpanBeforeBarrierKernel(int* buffer)
    {
        {
            Span<int> localSpan = new Span<int>(buffer, 10);
            localSpan[0] = 100;
        }

        // Barrier is called after the ref struct has left its scope
        Gpu.SyncThreads();

        {
            Span<int> localSpan = new Span<int>(buffer, 10);
            localSpan[0] += 200;
        }
    }

    [GpuKernel]
    public static void SafeGotoWithinPhaseKernel(int[] buffer)
    {
        buffer[0] = 1;
        goto LabelInSamePhase;

    LabelInSamePhase:
        buffer[0] = 2;
        Gpu.SyncThreads();
        buffer[0] = 3;
    }
}
