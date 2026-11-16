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
/// Differential kernels for C# 15 labeled <c>break</c> and <c>continue</c> statements (#1990).
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

    // The kernels from here hold a split point (a barrier, a Gpu.ForUnroll body that holds one, a Gpu.SyncGrid body, a
    // warp intrinsic, a call that suspends) in the labeled loop or switch. Every jump depends on the loop counters and on
    // u (the first input, the same for every thread) only, so every thread of a block reaches each barrier.

    [GpuDeviceFunction]
    public static void SuspendingStep(int[] r, int tid, int step)
    {
        Gpu.SyncThreads();
        r[tid] += step;
    }

    [GpuKernel]
    public static void BarrierLabeledKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int t = ThreadIndex.X;
        int s = 0;

        outer:
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                Gpu.SyncThreads();
                if (j == 1 && i == 2) break outer;
                if (j == 2) continue outer;
                s += (i * 4) + j;
            }

            s += 100;
        }

        r[tid] = s + t;
    }

    [GpuKernel]
    public static void BarrierInTheOuterBodyOnly(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;

        outer:
        for (int i = 0; i < 4; i++, s++)
        {
            Gpu.SyncThreads();
            for (int j = 0; j < 4; j++)
            {
                if (j == 1 && ((i + u) & 1) == 0)
                {
                    s += 10 + (x & 3);
                    continue outer;
                }

                if (i == 2 && j == 2)
                {
                    s += 1000;
                    break outer;
                }

                s += (x & 3) + j;
            }

            s += 100;
        }

        r[tid] = s;
    }

    [GpuKernel]
    public static void BarrierOutsideTheLabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;

        for (int i = 0; i < 3; i++)
        {
            Gpu.SyncThreads();
            inner:
            for (int j = 0; j < 4; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (k == 1 && ((j + u) & 1) == 0)
                    {
                        s += 5 + (x & 3);
                        continue inner;
                    }

                    if (j == 2 && k == 2)
                    {
                        s += 70;
                        break inner;
                    }

                    s += j + k + i;
                }

                s += 7;
            }

            s += 100;
        }

        r[tid] = s;
    }

    [GpuKernel]
    public static void SwitchWithABarrierInALabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;

        outer:
        for (int i = 0; i < 5; i++)
        {
            sw:
            switch ((i + u) & 3)
            {
                case 0:
                    Gpu.SyncThreads();
                    s += 1 + (x & 1);
                    continue outer;
                case 1:
                    Gpu.SyncThreads();
                    if (i >= 2) break outer;
                    s += 2;
                    break;
                case 2:
                    for (int j = 0; j < 3; j++)
                    {
                        Gpu.SyncThreads();
                        if (j == 1) break sw;
                        if ((i & 1) == 1) continue outer;
                        s += 4 + j;
                    }

                    s += 30;
                    break;
                default:
                    s += 8;
                    break;
            }

            s += 100;
        }

        r[tid] = s;
    }

    [GpuKernel]
    public static void DoWhileWithABarrier(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;
        int n = 0;

        outer:
        do
        {
            for (int j = 0; j < 3; j++)
            {
                Gpu.SyncThreads();
                if (j == 1 && ((n + u) & 1) == 0)
                {
                    s += 10 + n;
                    continue outer;
                }

                if (n == 2 && j == 2)
                {
                    s += 100 + (x & 3);
                    break outer;
                }

                s += n + j + 1;
            }

            s += 40;
        } while (++n < 4);

        r[tid] = (n * 1000) + s;
    }

    [GpuKernel]
    public static void ForEachWithABarrier(int[] xs, int[] items, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int s = 0;

        outer:
        foreach (int v in items)
        {
            for (int j = 0; j < 2; j++)
            {
                Gpu.SyncThreads();
                if (j == 0 && (v & 1) == 0)
                {
                    s += (v * 3) + (x & 1);
                    continue outer;
                }

                if (j == 1 && v >= 6)
                {
                    s += v * 7;
                    break outer;
                }

                s += v + j;
            }

            s += 15;
        }

        r[tid] = s;
    }

    [GpuKernel]
    public static void ForUnrollBarrierInALabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;

        outer:
        for (int i = 0; i < 3; i++)
        {
            Gpu.ForUnroll(0, 2, k =>
            {
                Gpu.SyncThreads();
                s += k + 1;
            });
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((i + u) & 1) == 0)
                {
                    s += 10 + (x & 3);
                    continue outer;
                }

                if (i == 2 && j == 2)
                {
                    s += 100;
                    break outer;
                }

                s += j + 1;
            }

            s += 50;
        }

        r[tid] = s;
    }

    [GpuKernel]
    public static void SuspendingCallInALabeledLoop(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        r[tid] = 0;

        outer:
        for (int i = 0; i < 3; i++)
        {
            SuspendingStep(r, tid, 1);
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((i + u) & 1) == 1)
                {
                    r[tid] += 10 + (x & 3);
                    continue outer;
                }

                if (i == 2 && j == 2)
                {
                    r[tid] += 100;
                    break outer;
                }

                r[tid] += (j + 1) * 1000;
            }

            r[tid] += 50;
        }
    }

    [GpuKernel(Cooperative = true)]
    public static void SyncGridBodyInALabeledLoop(int[] xs, int[] r)
    {
        int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[gid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;
        r[gid] = 0;

        outer:
        for (int i = 0; i < 4; i++)
        {
            Gpu.SyncGrid(() =>
            {
                r[gid] += 1;
            });
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && i == (u & 1))
                {
                    s += 10 + (x & 3);
                    continue outer;
                }

                if (i == 3 && j == 1)
                {
                    s += 200;
                    break outer;
                }

                s += (i * 3) + j + 1;
            }

            s += 50;
        }

        r[gid] += s * 100;
    }

    // The condition of the labeled loop holds the split point
    [GpuKernel]
    public static void WarpShuffleInTheLabeledLoopCondition(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int u = xs[0] & 7;
        int s = 0;

        outer:
        for (int i = 0; Warp.ShuffleSync(0xFFFFFFFFu, i, 0) < 4; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((i + u) & 1) == 0)
                {
                    s += 10 + (x & 3);
                    continue outer;
                }

                if (i == 3 && j == 2)
                {
                    s += 100;
                    break outer;
                }

                s += j + 1;
            }

            s += 50;
        }

        r[tid] = s;
    }

    // Lane t leaves the loop by break outer in the iteration t % 4 (the lanes with bit 2 set continue outer instead, so
    // they never get there). The lanes meet again where the loop ends, each outer iteration starts with the lanes that are
    // still in the loop, and the lanes that run the inner loop to its end read the mask after it together.
    [GpuKernel]
    public static void ActiveMaskAfterALabeledBreak(uint[] result)
    {
        int t = ThreadIndex.X;

        outer:
        for (int i = 0; i < 3; i++)
        {
            result[(t * 8) + i] = Warp.ActiveMask();
            for (int j = 0; j < 2; j++)
            {
                if (((t >> 2) & 1) == 1 && j == 0) continue outer;
                if (i == t % 4 && j == 1) break outer;
            }

            result[(t * 8) + 3 + i] = Warp.ActiveMask();
        }

        result[(t * 8) + 6] = Warp.ActiveMask();
    }

#pragma warning disable GSH0033 // Dynamic control flow inside Gpu.Hint.Unroll is the subject of this test
    [GpuKernel]
    public static void UnrolledLabeledLoops(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int acc = 0;

        Gpu.Hint.Unroll();
        outerFor:
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (j == 1 && ((x + i) & 1) == 0)
                {
                    acc += 10 + (i * 3) + j;
                    continue outerFor;
                }

                if (i == 2 && j == 2 && (x & 2) != 0)
                {
                    acc += 100 + (i * 3) + j;
                    break outerFor;
                }

                acc += i + j + 1;
            }

            acc += 20;
        }

        int w = 0;
        Gpu.Hint.Unroll(2);
        outerWhile:
        while (w < 4)
        {
            w++;
            for (int j = 0; j < 2; j++)
            {
                if (j == 0 && ((x + w) & 1) != 0)
                {
                    acc += (w * 5) + j;
                    continue outerWhile;
                }

                if (w == 3 && j == 1 && (x & 4) != 0)
                {
                    acc += (w * 50) + j;
                    break outerWhile;
                }

                acc += w + j;
            }

            acc += 30;
        }

        int d = 0;
        Gpu.Hint.Unroll(4);
        outerDo:
        do
        {
            for (int j = 0; j < 2; j++)
            {
                if (j == 0 && ((x + d) & 2) != 0)
                {
                    acc += (d * 7) + j + 1;
                    continue outerDo;
                }

                if (d == 2 && j == 1 && (x & 8) != 0)
                {
                    acc += (d * 70) + j + 1;
                    break outerDo;
                }

                acc += d + j + 2;
            }

            acc += 40;
        } while (++d < 4);

        Gpu.Hint.Unroll(2);
        a:
        b:
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                if (j == 0 && ((x + i) & 1) == 0)
                {
                    acc += 15 + i;
                    continue b;
                }

                if (i == 1 && j == 1 && (x & 16) != 0)
                {
                    acc += 150 + j;
                    break b;
                }

                acc += i + j + 3;
            }

            acc += 50;
        }

        r[tid] = acc;
    }
#pragma warning restore GSH0033
}
