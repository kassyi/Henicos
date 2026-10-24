// ReSharper disable All
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct IterInner
{
    public int A;
}

public struct IterOuter
{
    public IterInner Inner;
    public int B;
}

public static partial class IteratorKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Range(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Evens(int[] data, int length)
    {
        foreach (var x in data)
        {
            if (x % 2 == 0)
            {
                yield return x;
            }
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Fibonacci(int n)
    {
        int a = 0;
        int b = 1;
        for (int i = 0; i < n; i++)
        {
            yield return a;
            int next = a + b;
            a = b;
            b = next;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Countdown(int n)
    {
        while (n-- > 0)
        {
            yield return n;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> TakeUntil(int[] data, int limit)
    {
        foreach (var x in data)
        {
            if (x > limit)
            {
                yield break;
            }

            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Doubled(int start, int count)
    {
        foreach (var x in Range(start, count))
        {
            yield return x * 2;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<IterOuter> StructItems(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new IterOuter
            {
                Inner = new IterInner { A = start + i },
                B = (i + 1) * 10
            };
        }
    }

    [GpuKernel]
    public static void RangeKernel(int[] output, int count)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var x in Range(tid, count))
        {
            sum += x;
        }

        output[tid] = sum;
    }

    [GpuKernel]
    public static void EvensKernel(int[] data, int length, int[] output)
    {
        int sum = 0;
        foreach (var x in Evens(data, length))
        {
            sum += x;
        }

        output[0] = sum;
    }

    [GpuKernel]
    public static void FibonacciKernel(int n, int[] lastOutput, int[] sumOutput)
    {
        int last = 0;
        int sum = 0;
        foreach (var x in Fibonacci(n))
        {
            last = x;
            sum += x;
        }

        lastOutput[0] = last;
        sumOutput[0] = sum;
    }

    [GpuKernel]
    public static void CountdownTwiceKernel(int n, int[] firstSum, int[] secondSum)
    {
        IEnumerable<int> e = Countdown(n);
        int sum1 = 0;
        foreach (var x in e)
        {
            sum1 += x;
        }

        int sum2 = 0;
        foreach (var x in e)
        {
            sum2 += x;
        }

        firstSum[0] = sum1;
        secondSum[0] = sum2;
    }

    [GpuKernel]
    public static void TakeUntilKernel(int[] data, int limit, int[] output)
    {
        int sum = 0;
        foreach (var x in TakeUntil(data, limit))
        {
            sum += x;
        }

        output[0] = sum;
    }

    [GpuKernel]
    public static void DoubledKernel(int[] output, int count)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var x in Doubled(tid, count))
        {
            sum += x;
        }

        output[tid] = sum;
    }

    [GpuKernel]
    public static void StructIteratorKernel(int[] output, int count)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var item in StructItems(tid, count))
        {
            sum += item.Inner.A + item.B;
        }

        output[tid] = sum;
    }
}
