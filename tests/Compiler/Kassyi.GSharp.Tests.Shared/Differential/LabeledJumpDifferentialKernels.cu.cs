#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

internal struct LabeledJumpLifoRecorder : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Id;

    public void Dispose()
    {
        Log[Slot] = (Log[Slot] * 10) + Id;
    }
}

/// <summary>
/// Differential kernels for C# 15 labeled <c>break</c> and <c>continue</c> statements (#1990, design.md §6.3 PR 1 rows).
/// </summary>
public static partial class LabeledJumpDifferentialKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> YieldTwoWithInnerLabeledLoop(int seed)
    {
        int acc = seed & 15;
        innerLoop:
        for (int a = 0; a < 3; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                if (b == 0 && ((seed + a) & 1) == 0)
                {
                    acc += 10 + a;
                    continue innerLoop;
                }

                if (a == 2 && b == 1 && (seed & 2) != 0)
                {
                    acc += 100 + b;
                    break innerLoop;
                }

                acc += a + b + 1;
            }

            yield return acc + a;
        }

        yield return acc + 500;
    }

    [GpuKernel]
    public static void BreakAndContinueOuterFor(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        outer:
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (j == 1 && ((x + i) & 1) == 0)
                {
                    acc += 100 + (i * 10) + j;
                    continue outer;
                }

                if (i == 3 && j == 2 && (x & 2) != 0)
                {
                    acc += 1000 + (i * 10) + j;
                    break outer;
                }

                acc += (i * 5) + j + 1;
            }

            acc += 50;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void LabeledWhile(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;
        int i = 0;

        outer:
        while (i < 5)
        {
            i++;
            int j = 0;
            int safety = 0;
            while (j < 3 && ++safety < 32)
            {
                if (j == 1 && ((x + i) & 1) == 0)
                {
                    acc += (10 * i) + j;
                    continue outer;
                }

                if (i == 4 && j == 2 && (x & 2) != 0)
                {
                    acc += (100 * i) + j;
                    break outer;
                }

                acc += i + j;
                j++;
            }

            acc += 30;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void SwitchInLabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        outer:
        for (int i = 0; i < 5; i++)
        {
            switch ((x + i) & 3)
            {
                case 0:
                    acc += (10 * i) + 1;
                    continue outer;
                case 1:
                    if (i >= 2)
                    {
                        acc += (100 * i) + 2;
                        break outer;
                    }

                    acc += i + 3;
                    break;
                default:
                    acc += i + 5;
                    break;
            }

            acc += 40;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void ContinueOuterRunsTheIncrement(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;
        int steps = 0;

        outer:
        for (int i = 0; i < 4; i++, steps++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((x + i) & 1) == 0)
                {
                    acc += 10 + i;
                    continue outer;
                }

                acc += i + j + 1;
            }

            int afterInner = i + 20;
            acc += afterInner;
        }

        r[tid] = (steps * 1000) + acc;
    }

    [GpuKernel]
    public static void ContinueOuterEvaluatesTheDoCondition(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;
        int n = 0;
        int limit = 3 + (x & 1);

        outer:
        do
        {
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((x + n) & 1) == 0)
                {
                    acc += (10 * n) + j;
                    continue outer;
                }

                if (n == 2 && j == 2 && (x & 2) != 0)
                {
                    acc += (100 * n) + j;
                    break outer;
                }

                acc += n + j + 1;
            }

            acc += 40;
        } while (++n < limit);

        r[tid] = (n * 1000) + acc;
    }

    [GpuKernel]
    public static void MiddleOfThreeLoops(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        top:
        for (int i = 0; i < 3; i++)
        {
            mid:
            for (int j = 0; j < 3; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int mode = (x + (i * 9) + (j * 3) + k) & 3;
                    if (mode == 0 && k == 1)
                    {
                        acc += 10 + i + j;
                        continue mid;
                    }

                    if (mode == 1 && k == 2)
                    {
                        acc += 100 + i + j;
                        break mid;
                    }

                    if (mode == 2 && i == 1 && j == 2 && k == 1)
                    {
                        acc += 500;
                        continue top;
                    }

                    acc += i + j + k + 1;
                }

                acc += 30;
            }

            acc += 300;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void TwoLoopsWithTheSameLabel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        {
            outer:
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (j == 0 && ((x + i) & 1) == 0)
                    {
                        acc += 10 + i;
                        continue outer;
                    }

                    if (i == 2 && j == 1)
                    {
                        acc += 100 + j;
                        break outer;
                    }

                    acc += i + j + 1;
                }

                acc += 20;
            }
        }

        {
            outer:
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (j == 0 && ((x + i) & 1) != 0)
                    {
                        acc += 30 + i;
                        continue outer;
                    }

                    if (i == 1 && j == 1)
                    {
                        acc += 300 + j;
                        break outer;
                    }

                    acc += (i * 2) + j + 1;
                }

                acc += 40;
            }
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void ForEachOverAnArray(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        Span<int> intItems = stackalloc int[4]
        {
            (x & 7) + 1,
            ((x >> 2) & 7) + 1,
            ((x >> 4) & 7) + 1,
            ((x >> 6) & 7) + 1
        };
        Span<float> floatItems = stackalloc float[4]
        {
            (x & 7) + 1.25f,
            ((x >> 2) & 7) + 2.5f,
            ((x >> 4) & 7) + 3.75f,
            ((x >> 6) & 7) + 4.25f
        };

        int acc = 0;

        outerInt:
        foreach (int v in intItems)
        {
            for (int j = 0; j < 2; j++)
            {
                if (j == 0 && (v & 1) == 0)
                {
                    acc += v * 10;
                    continue outerInt;
                }

                if (j == 1 && v >= 7)
                {
                    acc += v * 100;
                    break outerInt;
                }

                acc += v + j;
            }

            acc += 15;
        }

        outerConverted:
        foreach (int v in floatItems)
        {
            for (int j = 0; j < 2; j++)
            {
                if (j == 0 && (v & 1) == 0)
                {
                    acc += v * 13;
                    continue outerConverted;
                }

                if (j == 1 && v >= 8)
                {
                    acc += v * 130;
                    break outerConverted;
                }

                acc += v + j;
            }

            acc += 25;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void ForEachOverAnIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        outer2:
        for (int pass = 0; pass < 2; pass++)
        {
            outer:
            foreach (int item in YieldTwoWithInnerLabeledLoop(x + pass))
            {
                for (int k = 0; k < 3; k++)
                {
                    if (k == 1 && ((item + x) & 1) == 0)
                    {
                        acc += item * 3;
                        continue outer;
                    }

                    if (k == 2 && item > 500 && (x & 2) == 0)
                    {
                        acc += item * 5;
                        break outer;
                    }

                    acc += item + k;
                }

                if (pass == 0 && item > 500 && (x & 4) != 0)
                {
                    acc += 777;
                    break outer2;
                }

                acc += 40;
            }

            acc += 100;
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void BreakLabeledSwitchFromALoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        sw:
        switch (x & 3)
        {
            case 0:
                for (int j = 0; j < 3; j++)
                {
                    if (j == 1)
                    {
                        acc += 10;
                        break sw;
                    }

                    acc += j + 1;
                }

                acc += 100;
                break;
            case 1:
                switch ((x >> 2) & 1)
                {
                    case 0:
                        acc += 20;
                        break sw;
                    default:
                        acc += 30;
                        break;
                }

                acc += 200;
                break;
            case 2:
                for (int j = 0; j < 2; j++)
                {
                    acc += 5 + j;
                }

                acc += 300;
                break;
            default:
                acc += 40;
                break;
        }

        r[tid] = acc + 7;
    }

    [GpuKernel]
    public static void UsingInsideTheLabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int slot = tid * 2;
        r[slot] = 0;
        int acc = 0;

        outer:
        for (int i = 0; i < 3; i++)
        {
            using var a = new LabeledJumpLifoRecorder { Log = r, Slot = slot, Id = 1 };
            for (int j = 0; j < 2; j++)
            {
                using var b = new LabeledJumpLifoRecorder { Log = r, Slot = slot, Id = 2 };
                if (j == 0 && ((x + i) & 1) == 0)
                {
                    acc += 10 + i;
                    continue outer;
                }

                if (i == 1 && j == 1 && (x & 2) != 0)
                {
                    acc += 100 + j;
                    break outer;
                }

                acc += i + j + 1;
            }

            acc += 30;
        }

        r[slot + 1] = acc;
    }

    [GpuKernel]
    public static void LabeledLoopInAKernelWithoutABarrier(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        Gpu.ForUnroll(0, 2, outer =>
        {
            outer:
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (j == 0 && ((x + outer + i) & 1) == 0)
                    {
                        acc += ((outer + 1) * 10) + i;
                        continue outer;
                    }

                    if (i == 2 && j == 1)
                    {
                        acc += ((outer + 1) * 100) + j;
                        break outer;
                    }

                    acc += outer + i + j + 1;
                }

                acc += 25;
            }
        });

        strLoop:
        foreach (char c in "ab\0cd")
        {
            for (int j = 0; j < 2; j++)
            {
                if (c == '\0' && j == 0 && (x & 1) == 0)
                {
                    acc += 100 + j;
                    continue strLoop;
                }

                if (c == 'c' && j == 1 && (x & 2) != 0)
                {
                    acc += 1000 + j;
                    break strLoop;
                }

                acc += c + j;
            }

            acc += 15;
        }

        Gpu.Hint.Unroll(2);
        for (int u = 0; u < 2; u++)
        {
            innerUnroll:
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (j == 0 && ((x + u + i) & 1) == 0)
                    {
                        acc += ((u + 1) * 7) + i;
                        continue innerUnroll;
                    }

                    if (i == 2 && j == 1)
                    {
                        acc += ((u + 1) * 70) + j;
                        break innerUnroll;
                    }

                    acc += u + i + j + 1;
                }

                acc += 11;
            }
        }

        r[tid] = acc;
    }

    [GpuKernel]
    public static void UnlabeledJumpsNextToLabeledOnes(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        outer:
        for (int i = 0; i < 4; i++)
        {
            if (i == 0 && (x & 1) == 0)
            {
                acc += 5;
                continue outer;
            }

            for (int j = 0; j < 4; j++)
            {
                int mode = (x + (i * 4) + j) & 3;
                if (mode == 0 && j == 1)
                {
                    acc += 10;
                    continue;
                }

                if (mode == 1 && j == 2)
                {
                    acc += 20;
                    break;
                }

                if (mode == 2 && j == 1)
                {
                    acc += 30;
                    continue outer;
                }

                if (mode == 3 && i == 2 && j == 2)
                {
                    acc += 400;
                    break outer;
                }

                acc += i + j + 1;
            }

            if (i == 3 && (x & 4) != 0)
            {
                acc += 50;
                break outer;
            }

            acc += 100;
        }

        r[tid] = acc;
    }
}
