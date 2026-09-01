// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class GSH_0007_ForEachProhibitionKernels
{
    // GSH-0007 Happy Path: Standard for loop replacing foreach
    [GpuKernel]
    public static void StandardForLoopKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            int sum = 0;
            for (int i = 0; i < length; i++)
            {
                sum = sum + input[i];
            }
            output[tid] = sum + input[tid];
        }
    }
}
