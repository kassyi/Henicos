#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public partial class UnrollControlFlowScope_LabeledJump
{
    [GpuKernel]
    public static void LabeledBreakLeavingUnrolledLoop(int[] data)
    {
        outer:
        for (int a = 0; a < 2; a++)
        {
            Gpu.Hint.Unroll(4);
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (data[i + j] == 0)
                    {
                        break outer;
                    }
                }
            }
        }
    }

    [GpuKernel]
    public static void LabeledBreakStayingInsideUnrolledLoop(int[] data)
    {
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            inner:
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    if (data[i + j + k] == 0)
                    {
                        break inner;
                    }
                }
            }
        }
    }
}
