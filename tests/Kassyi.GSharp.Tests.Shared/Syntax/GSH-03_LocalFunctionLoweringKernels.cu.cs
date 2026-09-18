// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class GSH_03_LocalFunctionLoweringKernels
{
    [GpuKernel]
    public static void LocalFunctionVoidKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        void MultiplyByTwo()
        {
            data[tid] = data[tid] * 2;
        }

        MultiplyByTwo();
    }

    [GpuKernel]
    public static void LocalFunctionValueReturnKernel(int[] data, int bonus)
    {
        int tid = ThreadIndex.X;

        int ComputeTotal(int val)
        {
            return val * 3 + bonus;
        }

        data[tid] = ComputeTotal(data[tid]);
    }

    [GpuKernel]
    public static void LocalFunctionExpressionBodyKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        int Square(int x) => x * x;

        data[tid] = Square(data[tid]);
    }

    [GpuKernel]
    public static void NestedLocalFunctionKernel(int[] data)
    {
        int tid = ThreadIndex.X;

        int InnerAdd(int a, int b) => a + b;

        int OuterMultiplyAndAdd(int x)
        {
            int doubled = x * 2;
            return InnerAdd(doubled, 10);
        }

        data[tid] = OuterMultiplyAndAdd(data[tid]);
    }

    [GpuKernel]
    public static void MultiParamLocalFunctionKernel(int[] a, int[] b, int[] result)
    {
        int tid = ThreadIndex.X;

        int ClampAdd(int val1, int val2, int minVal, int maxVal)
        {
            int sum = val1 + val2;
            if (sum < minVal) return minVal;
            if (sum > maxVal) return maxVal;
            return sum;
        }

        result[tid] = ClampAdd(a[tid], b[tid], 0, 100);
    }
}
