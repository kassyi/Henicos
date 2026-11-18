using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

public static partial class ReconvergenceJumpNestedFunctionKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionWithRejectedGotoAndActiveMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        result[t] = Warp.ActiveMask();

        void LocalHelper(int flag)
        {
            int n = 0;
            if (flag != 0)
            {
                goto mid;
            }

        top:
            result[t + 32] = Warp.ActiveMask();
        mid:
            result[t + 64] = 1u;
            if (n++ < 1)
            {
                goto top;
            }
        }

        LocalHelper(t & 1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LambdaWithRejectedGotoAndActiveMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        result[t] = Warp.ActiveMask();

        Action<int> helper = flag =>
        {
            int n = 0;
            if (flag != 0)
            {
                goto mid;
            }

        top:
            result[t + 32] = Warp.ActiveMask();
        mid:
            result[t + 64] = 1u;
            if (n++ < 1)
            {
                goto top;
            }
        };

        helper(t & 1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SingleThreadLambdaWithRejectedGotoAndActiveMaskKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        result[t] = Warp.ActiveMask();

        Gpu.SingleThread(() =>
        {
            int n = 0;
            if ((t & 1) != 0)
            {
                goto mid;
            }

        top:
            result[t + 32] = Warp.ActiveMask();
        mid:
            result[t + 64] = 1u;
            if (n++ < 1)
            {
                goto top;
            }
        });
    }
}
