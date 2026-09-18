// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_156_LoopUnrollStatements
{
    [GpuKernel]
    public static void StatementUnrollFor(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll();
        for (int i = 0; i < 4; i++)
        {
            sum += i + 1;
        }
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollFactor(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < 4; i++)
        {
            sum += (i + 1) * 10;
        }
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollWhile(int[] data)
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

    [GpuKernel]
    public static void StatementUnrollNested(int[] data)
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

    [GpuKernel]
    public static void StatementUnrollSingleStatement(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll();
        for (int i = 0; i < 4; i++)
            sum += 2;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollFactor0(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(0);
        for (int i = 0; i < 4; i++)
            sum += i;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollFactor1(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(1);
        for (int i = 0; i < 4; i++)
            sum += i;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollFactorMax(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(int.MaxValue);
        for (int i = 0; i < 4; i++)
            sum += i;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollNegativeFactor(int[] data)
    {
        int sum = 0;
        Gpu.Hint.Unroll(-1);
        for (int i = 0; i < 4; i++)
            sum += i;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollVariableFactor(int[] data)
    {
        int sum = 0;
        int factor = 4;
        Gpu.Hint.Unroll(factor);
        for (int i = 0; i < 4; i++)
            sum += i;
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollMisplacedEnd(int[] data)
    {
        int sum = 0;
        for (int i = 0; i < 4; i++)
            sum += i;
        Gpu.Hint.Unroll();
        data[0] = sum;
    }

    [GpuKernel]
    public static void StatementUnrollMisplacedLast(int[] data)
    {
        data[0] = 1;
        Gpu.Hint.Unroll();
    }
}
