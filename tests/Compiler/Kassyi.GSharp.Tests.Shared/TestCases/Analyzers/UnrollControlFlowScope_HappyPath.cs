using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public partial class UnrollControlFlowScope_HappyPath
{
    [GpuKernel]
    public static void SwitchBreakKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            switch (data[i])
            {
                case 0:
                    data[i] = 1;
                    break;
                default:
                    break;
            }
        }
    }

    [GpuKernel]
    public static void InnerLoopBreakContinueKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                if (data[j] == 0)
                {
                    break;
                }
                if (data[j] == 1)
                {
                    continue;
                }
            }
        }
    }

    [GpuKernel]
    public static void LambdaReturnKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            Action action = () =>
            {
                return;
            };
            action();
        }
    }
}
