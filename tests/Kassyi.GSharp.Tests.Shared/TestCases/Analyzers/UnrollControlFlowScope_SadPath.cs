using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public partial class UnrollControlFlowScope_SadPath
{
    [GpuKernel]
    public static void ReturnInInnerLoopKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                if (data[j] == 0)
                {
                    return;
                }
            }
        }
    }

    [GpuKernel]
    public static void GotoOutsideKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            if (data[i] == 0)
            {
                goto Exit;
            }
        }

    Exit:
        return;
    }

    [GpuKernel]
    public static void DirectBreakKernel(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            if (data[i] == 0)
            {
                break;
            }
        }
    }
}
