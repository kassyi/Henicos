using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

[SkipSnapshotConsistency("Sad Path")]
public static partial class ReconvergenceJumpNestedFunctionKernels
{
    [GpuKernel]
    public static void LocalFunctionMiddleGotoKernel(uint[] result)
    {
        uint Run(int t)
        {
            int n = 0;
            uint m = 0u;
        top:
            n++;
        mid:
            m = Warp.ActiveMask();
            if ((t & 1) == 1 && n < 2)
            {
                goto top;
            }

            if ((t & 2) != 0 && n < 3)
            {
                goto mid;
            }

            return m;
        }

        int tid = ThreadIndex.X;
        result[tid] = Warp.ActiveMask() ^ Run(tid);
    }

    [GpuKernel]
    public static void LambdaMiddleGotoKernel(uint[] result)
    {
        Func<int, uint> run = t =>
        {
            int n = 0;
            uint m = 0u;
        top:
            n++;
        mid:
            m = Warp.ActiveMask();
            if ((t & 1) == 1 && n < 2)
            {
                goto top;
            }

            if ((t & 2) != 0 && n < 3)
            {
                goto mid;
            }

            return m;
        };

        int tid = ThreadIndex.X;
        result[tid] = Warp.ActiveMask() ^ run(tid);
    }

    [GpuKernel]
    public static void SingleThreadMiddleGotoKernel(uint[] result)
    {
        int n = 0;
        uint m = 0u;
        Gpu.SingleThread(() =>
        {
        top:
            n++;
        mid:
            m = Warp.ActiveMask();
            if (n < 2)
            {
                goto top;
            }

            if (n < 3)
            {
                goto mid;
            }
        });

        int tid = ThreadIndex.X;
        result[tid] = Warp.ActiveMask() ^ m;
    }

    [GpuKernel]
    public static void MiddleGotoAroundLocalFunctionSplitKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        if ((tid & 2) != 0)
        {
            goto after;
        }

    before:
        uint LocalSplit() => Warp.ActiveMask();
    after:
        Func<uint> lambdaSplit = () => Warp.ActiveMask();
        if ((tid & 1) != 0)
        {
            goto before;
        }

        result[tid] = LocalSplit() ^ lambdaSplit() ^ Warp.ActiveMask();
    }
}
