#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public partial class UnrollControlFlowScope_LabeledLoop
{
    [GpuKernel]
    public static void UnrollBeforeLabeledLoops(int[] data)
    {
        Gpu.Hint.Unroll();
        outer:
        for (int i = 0; i < 4; i++)
        {
            data[i] += 1;
        }

        int wCount = 0;
        Gpu.Hint.Unroll(2);
        w:
        while (wCount < 4)
        {
            data[wCount] += 2;
            wCount++;
        }

        int dCount = 0;
        Gpu.Hint.Unroll(4);
        d:
        do
        {
            data[dCount] += 3;
            dCount++;
        } while (dCount < 4);

        Gpu.Hint.Unroll();
        a:
        b:
        for (int i = 0; i < 4; i++)
        {
            data[i] += 4;
        }
    }

    [GpuKernel]
    public static void BreakOutOfUnrolledLabeledLoop(int[] data)
    {
        Gpu.Hint.Unroll(4);
        outer:
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
