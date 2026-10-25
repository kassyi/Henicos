using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

public static class DupSpanDemo
{
    [GpuKernel]
    public static void DupSpanDemoKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        {
            int s = tid * 2;
            Span<int> view = output;
            Gpu.SyncThreads();
            view = output;
            view[tid] = s;
        }
    }
}
