using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

[InlineArray(4)]
public struct FixedArray4
{
    private int _element0;
}

public static partial class SYN_70_72_LoopsAndJumpsKernels
{
    [GpuKernel]
    public static void ForeachInlineArrayKernel(int[] output)
    {
        FixedArray4 arr = new FixedArray4();
        arr[0] = 10;
        arr[1] = 20;
        arr[2] = 30;
        arr[3] = 40;

        int total = 0;
        foreach (var val in arr)
        {
            total += val;
        }

        output[0] = total;
    }

    [GpuKernel]
    public static void ForeachCollectionExpressionKernel(int[] output)
    {
        ReadOnlySpan<int> arr = [10, 20, 30, 40];

        int total = 0;
        foreach (var val in arr)
        {
            total += val;
        }

        output[0] = total;
    }

    [GpuKernel]
    public static void ForeachStackAllocKernel(int[] output)
    {
        Span<int> arr = stackalloc int[] { 10, 20, 30, 40 };

        int total = 0;
        foreach (var val in arr)
        {
            total += val;
        }

        output[0] = total;
    }
}
