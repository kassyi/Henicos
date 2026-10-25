// ReSharper disable All
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct IterInner<T>
{
    public T A;

    public IterInner(T a)
    {
        A = a;
    }
}

public struct IterOuter
{
    public IterInner<int> Inner;
    public int B;

    public IterOuter(IterInner<int> inner, int b)
    {
        Inner = inner;
        B = b;
    }
}

public struct IterDisposable : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Marker;

    public void Dispose()
    {
        Log[Slot] += Marker;
    }
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
            yield return new IterOuter(new IterInner<int>(start + i), (i + 1) * 10);
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
        n = 0;
        int sum1 = 0;
        foreach (var x in e)
        {
            sum1 += x;
        }

        int sum2 = 0;
        foreach (var x in (e))
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
            var wrapped = new IterInner<int>(item.Inner.A + item.B);
            sum += wrapped.A;
        }

        output[tid] = sum;
    }

#pragma warning disable GSH0072
    [GpuDeviceFunction]
    public static IEnumerable<int> MultiYieldAndSwitch(int mode, int baseVal)
    {
        int Scale(int v) => v * 10;

        int scaled = Scale(baseVal);
        switch (mode)
        {
            case 0:
                yield return scaled + 1;
                break;
            case 1:
                yield return scaled + 2;
                yield return scaled + 3;
                break;
            default:
                yield break;
        }

        yield return scaled + 9;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> UsingAndSpanIter(int[] data, int[] disposeLog)
    {
        using (var guard = new IterDisposable { Log = disposeLog, Slot = 0, Marker = 100 })
        {
            Span<int> firstView = data;
            yield return firstView[0] + firstView[1];

            Span<int> secondView = data;
            yield return secondView[2] + secondView[3];
        }
    }
#pragma warning restore GSH0072

    [GpuDeviceFunction]
    public static IEnumerable<int> StepRange(int start, int count = 3, int step = 2)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i * step;
        }
    }

    [GpuDeviceFunction]
    private static int RecordEval(int value, int slot, ref int stepCounter, int[] evalLog)
    {
        stepCounter += 1;
        evalLog[slot] = stepCounter;
        return value;
    }

    [GpuDeviceFunction]
    public static IEnumerable<long> Big()
    {
        yield return int.MaxValue;
    }

    [GpuKernel]
    public static void ConsumerControlFlowKernel(int stopAt, int skipVal, int earlyReturnAt, int[] output)
    {
        int sum = 0;
        foreach (var x in Range(0, 8))
        {
            if (x == earlyReturnAt)
            {
                output[0] = sum + 1000;
                return;
            }

            if (x == skipVal)
            {
                continue;
            }

            int afterContinue = x;
            switch (afterContinue)
            {
                case 1:
                    sum += 10;
                    break;
                case 2:
                    sum += 20;
                    continue;
                default:
                    break;
            }

            for (int inner = 0; inner < 3; inner++)
            {
                if (inner == 1)
                {
                    break;
                }

                sum += 1;
            }

            if (afterContinue >= stopAt)
            {
                break;
            }

            sum += afterContinue;
        }

        output[0] = sum;
    }

    [GpuKernel]
    public static void MultiYieldLabelAndShadowKernel(int mode, int baseVal, int[] output)
    {
        int acc = 0;
        int scaled = 500;
        {
            foreach (var item in MultiYieldAndSwitch(mode, baseVal))
            {
                if (item < 0)
                {
                    goto SkipAdd;
                }

                foreach (var sub in MultiYieldAndSwitch(0, 1))
                {
                    if (sub < 0)
                    {
                        goto InnerDone;
                    }

                    if (item == 23 && sub == 19)
                    {
                        goto SkipAdd;
                    }

                    acc += sub;
                InnerDone:
                    ;
                }

                if (item == 999)
                {
                    goto Found;
                }

                acc += item + scaled;
            SkipAdd:
                acc += 1;
            }

        Found:
            output[0] = acc;
        }
    }

    [GpuKernel]
    public static void UsingAndSpanBreakKernel(int[] data, int maxItems, int[] output, int[] disposeLog)
    {
        int count = 0;
        int sum = 0;
        foreach (var v in UsingAndSpanIter(data, disposeLog))
        {
            sum += v;
            count += 1;
            if (count >= maxItems)
            {
                break;
            }
        }

        output[0] = sum;
    }

    [GpuKernel]
    public static void NamedAndDefaultArgsKernel(int[] output, int[] evalLog)
    {
        int counter = 0;
        int directSum = 0;
        foreach (var x in StepRange(
                     step: RecordEval(3, 0, ref counter, evalLog),
                     start: RecordEval(10, 1, ref counter, evalLog),
                     count: 3))
        {
            directSum += x;
        }

        var localSeq = StepRange(
            count: RecordEval(2, 2, ref counter, evalLog),
            start: RecordEval(20, 3, ref counter, evalLog));
        int localSum = 0;
        foreach (var y in (localSeq))
        {
            localSum += y;
        }

        int defaultSum = 0;
        foreach (var z in StepRange(5))
        {
            defaultSum += z;
        }

        output[0] = directSum;
        output[1] = localSum;
        output[2] = defaultSum;
    }

    [GpuKernel]
    public static void WideningElementTypeKernel(long[] output)
    {
        long sum = 0;
        foreach (var x in Big())
        {
            sum += x + 1;
        }

        output[0] = sum;
    }
}
