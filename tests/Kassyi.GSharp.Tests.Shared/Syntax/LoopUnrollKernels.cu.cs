// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class LoopUnrollKernels
{
    // Happy Path: Standard for loop with [GpuLoopUnroll]
    [GpuKernel]
    public static void LoopUnrollForKernel(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll();
        for (int i = 0; i < 4; i++)
        {
            sum += i + 1;
        }
        data[0] = sum;
    }

    // Happy Path: For loop with factor [GpuLoopUnroll(4)]
    [GpuKernel]
    public static void LoopUnrollFactorKernel(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            sum += (i + 1) * 10;
        }
        data[0] = sum;
    }

    // Happy Path: While loop with [GpuLoopUnroll]
    [GpuKernel]
    public static void LoopUnrollWhileKernel(int[] data)
    {
        int count = 0;
        int sum = 0;
        Gpu.Hint.Unroll();
        while (count < 3)
        {
            sum += 5;
            count++;
        }
        data[0] = sum;
    }

    // Edge Case: Nested loops where only inner loop has [GpuLoopUnroll]
    [GpuKernel]
    public static void LoopUnrollNestedKernel(int[] data)
    {
        int sum = 0;
        for (int i = 0; i < 2; i++)
        {
            Gpu.Hint.Unroll();
            for (int j = 0; j < 3; j++)
            {
                sum += (i + 1) * (j + 1);
            }
        }
        data[0] = sum;
    }

    // Edge Case: Single statement for loop omitting braces {}
    [GpuKernel]
    public static void LoopUnrollSingleStatementKernel(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll();
        for (int i = 0; i < 4; i++)
            sum += 2;
        data[0] = sum;
    }
}
