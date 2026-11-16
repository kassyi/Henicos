#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

internal struct UsingIteratorLifoRecorder : IDisposable
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
/// Differential kernels for jumps leaving a <c>using</c> statement or declaration from inside an inlined iterator
/// <c>foreach</c> body.
/// </summary>
public static partial class UsingIteratorExitDifferentialKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> YieldThree(int seed)
    {
        int baseVal = (seed & 7) + 1;
        yield return baseVal;
        yield return baseVal + 10;
        yield return baseVal + 20;
    }

    [GpuDeviceFunction]
    public static int ComputeWithUsingStatementReturn(int seed, int mode, int[] r, int slot)
    {
        using (var recorder = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 })
        {
            int acc = 0;
            int step = 0;
            foreach (var x in YieldThree(seed))
            {
                acc += x;
                if (step == mode)
                {
                    return x + 100;
                }

                step++;
            }

            return acc + 500;
        }
    }

    [GpuDeviceFunction]
    public static int ComputeWithNestedUsingReturn(int seed, int mode, int[] r, int slot)
    {
        using (var outer = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 })
        {
            using (var inner = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 2 })
            {
                int acc = 0;
                int step = 0;
                foreach (var x in YieldThree(seed))
                {
                    acc += x;
                    if (step == mode)
                    {
                        return x + 100;
                    }

                    step++;
                }

                return acc + 500;
            }
        }
    }

    [GpuKernel]
    public static void UsingStatementReturnFromIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int mode = x & 3;
        int slot = tid * 2;
        r[slot] = 0;
        r[slot + 1] = 0;

        using (var recorder = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 })
        {
            int step = 0;
            foreach (var item in YieldThree(x))
            {
                r[slot + 1] += item;
                if (step == mode)
                {
                    return;
                }

                step++;
            }

            r[slot + 1] += 200;
        }

        r[slot + 1] += 1000;
    }

    [GpuKernel]
    public static void UsingDeclarationReturnFromIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int mode = x & 3;
        int slot = tid * 2;
        r[slot] = 0;
        r[slot + 1] = 0;

        {
            using var recorder = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 };
            int step = 0;
            foreach (var item in YieldThree(x))
            {
                r[slot + 1] += item;
                if (step == mode)
                {
                    return;
                }

                step++;
            }

            r[slot + 1] += 200;
        }

        r[slot + 1] += 1000;
    }

    [GpuKernel]
    public static void DeviceFunctionReturnFromIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int mode = x & 3;
        int slot = tid * 2;
        r[slot] = 0;
        int first = ComputeWithUsingStatementReturn(x, mode, r, slot);
        int second = ComputeWithNestedUsingReturn(x + 1, (x >> 2) & 3, r, slot);
        r[slot + 1] = (first * 1000) + second;
    }

    [GpuKernel]
    public static void BreakContinueAndGotoOutsideUsingFromIterator(int[] xs, int[] r)
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
            using (var recorder = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = i + 1 })
            {
                int step = 0;
                foreach (var item in YieldThree(x + i))
                {
                    int action = (x + (i * 3) + step) & 7;
                    acc += item;
                    if (action == 0)
                    {
                        acc += 10;
                        continue outer;
                    }

                    if (action == 1 && i >= 1)
                    {
                        acc += 100;
                        break outer;
                    }

                    if (action == 2 && i == 2)
                    {
                        acc += 1000;
                        goto afterOuter;
                    }

                    step++;
                }

                acc += 50;
            }

            acc += 300;
        }

        acc += 5000;

        afterOuter:
        r[slot + 1] = acc;
    }

    [GpuKernel]
    public static void NestedUsingsReturnFromIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int mode = x & 3;
        int slot = tid * 2;
        r[slot] = 0;
        r[slot + 1] = 0;

        if ((x & 4) == 0)
        {
            using (var outer = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 })
            {
                using (var inner = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 2 })
                {
                    int step = 0;
                    foreach (var item in YieldThree(x))
                    {
                        r[slot + 1] += item;
                        if (step == mode)
                        {
                            return;
                        }

                        step++;
                    }

                    r[slot + 1] += 200;
                }
            }
        }
        else
        {
            using var first = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 3 };
            using var second = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 4 };
            int step = 0;
            foreach (var item in YieldThree(x))
            {
                r[slot + 1] += item;
                if (step == mode)
                {
                    return;
                }

                step++;
            }

            r[slot + 1] += 400;
        }

        r[slot + 1] += 1000;
    }

    [GpuKernel]
    public static void NormalCompletionWithoutReturnFromIterator(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int slot = tid * 2;
        r[slot] = 0;
        int acc = 0;

        using (var first = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 1 })
        {
            int step = 0;
            foreach (var item in YieldThree(x))
            {
                if (step == 0 && (x & 1) == 0)
                {
                    acc += 5;
                    step++;
                    continue;
                }

                acc += item;
                if (step == 1 && (x & 2) != 0)
                {
                    acc += 25;
                    break;
                }

                step++;
            }

            acc += 100;
        }

        {
            using var second = new UsingIteratorLifoRecorder { Log = r, Slot = slot, Id = 2 };
            foreach (var item in YieldThree(x + 3))
            {
                acc += item * 2;
            }

            acc += 300;
        }

        r[slot + 1] = acc;
    }
}
