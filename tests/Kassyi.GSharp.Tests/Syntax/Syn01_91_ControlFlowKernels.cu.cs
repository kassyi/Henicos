using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class Syn01_91_ControlFlowKernels
{
    [GpuKernel]
    public static void ComplexLogicKernel(int[] a, int[] b, int[] c)
    {
        for (int i = 0; i < 5; i++)
        {
            if (a[0] > 10)
            {
                c[0] = c[0] + a[0];
            }
            else
            {
                c[0] = c[0] + b[0];
            }
        }
    }

    [GpuKernel]
    public static void ControlFlowKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        // 1. switch
        switch (tid)
        {
            case 0:
                data[tid] = 10;
                break;
            case 1:
            case 2:
                data[tid] = 20;
                break;
            default:
                data[tid] = 30;
                break;
        }

        // 2. do-while
        int count = 0;
        do
        {
            count = count + 1;
        } while (count < 3);

        data[tid] = data[tid] + count;

        // 3. && and ||
        if (tid == 0 && count == 3)
        {
            data[tid] = data[tid] + 100;
        }

        if (tid == 1 || tid == 2)
        {
            data[tid] = data[tid] + 200;
        }
    }
}

