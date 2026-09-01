// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_03_InlineOutVarKernels
{
    [GpuDeviceFunction]
    public static bool TrySquare(int input, out int output)
    {
        output = input * input;
        return input > 0;
    }

    [GpuDeviceFunction]
    public static bool TryStep(int input, out int next)
    {
        next = input - 1;
        return next > 0;
    }

    // Happy Path: out var in method invocation arguments
    [GpuKernel]
    public static void HappyPathKernel(int[] input, int[] output, int[] success)
    {
        int tid = ThreadIndex.X;
        bool ok = TrySquare(input[tid], out var res);
        output[tid] = res;
        if (ok)
        {
            success[tid] = 1;
        }
        else
        {
            success[tid] = 0;
        }
    }

    // Edge Case 1: out var in if condition statement
    [GpuKernel]
    public static void IfConditionKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        if (TrySquare(input[tid], out var val))
        {
            output[tid] = val + 10;
        }
        else
        {
            output[tid] = val - 10;
        }
    }

    // Edge Case 2: out var in while loop condition statement
    [GpuKernel]
    public static void WhileConditionKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int curr = input[tid];
        int sum = 0;
        while (TryStep(curr, out var nextVal))
        {
            sum += nextVal;
            curr = nextVal;
        }
        output[tid] = sum;
    }
}
