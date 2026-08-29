// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class GSH_0008_LinqProhibitionKernels
{
    // GSH-0008 Happy Path: Standard imperative filtering and transformation loop replacing LINQ query expressions
    [GpuKernel]
    public static void StandardImperativeLoopKernel(int[] input, int[] output, int length)
    {
        int tid = ThreadIndex.X;
        if (tid < length)
        {
            // Imperative alternative to LINQ query: input.Where(x => x > 0).Select(x => x * 2)
            int val = input[tid];
            if (val > 0)
            {
                output[tid] = val * 2;
            }
            else
            {
                output[tid] = 0;
            }
        }
    }
}
