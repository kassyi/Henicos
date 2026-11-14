#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that pass a conditional that has a stack array local on a branch to an array parameter, and use it in other
/// ways: the stack array is a C++ array, which the C++ conditional would decay to a pointer, so each stack array branch
/// is written as the span it converts to.
/// </summary>
public static partial class ConditionalStackArrayKernels
{
    [GpuKernel]
    public static void ConditionalKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? a : input);
    }

    // The branches the other way round
    [GpuKernel]
    public static void ReversedKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? input : a);
    }

    // Two stack arrays of different sizes
    [GpuKernel]
    public static void DifferentSizesKernel(int[] flag, int[] output)
    {
        int[] a = new int[4];
        int[] b = [7, 8];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? a : b);
    }

    // Two stack arrays of one size, written by the callee: the write is seen through the array that was picked
    [GpuKernel]
    public static void EqualSizesWriterKernel(int[] flag, int[] output)
    {
        int[] a = new int[3];
        int[] b = new int[3];
        SetFirst(flag[0] > 1 ? a : b);
        output[0] = a[0];
        output[1] = b[0];
    }

    // A stack array of a size that is not a constant against a kernel array
    [GpuKernel]
    public static void NonConstantSizeKernel(int[] input, int[] flag, int count, int[] output)
    {
        int[] a = new int[count];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? a : input);
    }

    // A zero-length stackalloc span against a kernel array
    [GpuKernel]
    public static void EmptySpanKernel(int[] input, int[] flag, int[] output)
    {
        Span<int> empty = stackalloc int[0];
        output[0] = SumSpan(flag[0] > 1 ? empty : input);
    }

    // A conditional on the true side of another conditional, with a stack array on both arms of the inner one
    [GpuKernel]
    public static void NestedTrueSideKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        int[] b = [7, 8, 9, 10];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? (flag[1] > 1 ? a : b) : input);
    }

    // The arms of a switch expression
    [GpuKernel]
    public static void SwitchArmKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] switch { 1 => a, _ => input });
    }

    // Gpu.NotNull(a) as an arm
    [GpuKernel]
    public static void NotNullArmKernel(int[] input, int[] flag, int[] output)
    {
        int[] a = new int[4];
        a[1] = 5;
        output[0] = SumArray(flag[0] > 1 ? Gpu.NotNull(a) : input);
    }

    // The conditional of two stack arrays of one size in a var, and its length
    [GpuKernel]
    public static void VarLengthKernel(int[] flag, int[] output)
    {
        int[] a = [1, 2, 3];
        int[] b = [4, 5, 6];
        var picked = flag[0] > 1 ? a : b;
        output[0] = picked.Length * 10 + picked[1];
    }

    // The length of a parenthesized conditional, and an element written through it
    [GpuKernel]
    public static void MemberAccessKernel(int[] flag, int[] output)
    {
        int[] a = [1, 2, 3];
        int[] b = [4, 5, 6];
        (flag[0] > 1 ? a : b)[1] = 42;
        output[0] = (flag[0] > 1 ? a : b).Length;
        output[1] = a[1];
        output[2] = b[1];
    }

    [GpuDeviceFunction]
    private static int SumArray(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    [GpuDeviceFunction]
    private static int SumSpan(Span<int> values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    [GpuDeviceFunction]
    private static void SetFirst(int[] values)
    {
        values[0] = 9;
    }
}
