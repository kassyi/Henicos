using System;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;
using S = Kassyi.ZeroAlloc.Linq.StackLinqExtensions;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

internal static class StackLinqOrderTestExtensions
{
    [GpuDeviceFunction]
    public static int WeightedPair(this int first, int second) => first * 100 + second;

    [GpuDeviceFunction]
    public static void AddWeighted(ref this int target, int delta, int weight) => target += delta * weight;
}

[SkipSnapshotConsistency]
public static partial class StackLinqStaticFormKernels
{
    // 2a: Type-qualified static calls (fused)
    [GpuKernel]
    public static void StaticFusedChainKernel(int[] data, int limit, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        results[0] = StackLinqExtensions.Sum(StackLinqExtensions.AsStackEnumerable(data));
        results[1] = StackLinqExtensions.Sum(
            StackLinqExtensions.Select(
                StackLinqExtensions.Where(
                    StackLinqExtensions.AsStackEnumerable(data),
                    x => x > 2),
                x => x * 10));
        results[2] = StackLinqExtensions.Count(
            StackLinqExtensions.Take(
                StackLinqExtensions.AsStackEnumerable(data),
                limit),
            x => x % 2 == 0);
    }

    // 2b: Static calls of Contains and Aggregate (with and without seed, fused)
    [GpuKernel]
    public static void StaticContainsAndAggregateKernel(int[] data, int target, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        results[0] = StackLinqExtensions.Contains(StackLinqExtensions.AsStackEnumerable(data), target) ? 1 : 0;
        results[1] = StackLinqExtensions.Aggregate(StackLinqExtensions.AsStackEnumerable(data), (int acc, int x) => acc + x);
        results[2] = StackLinqExtensions.Aggregate(StackLinqExtensions.AsStackEnumerable(data), 100, (int acc, int x) => acc + x);
    }

    // 2c: Unfused static chains (local sequence variable and ElementAtOrDefault terminal)
    [GpuKernel]
    public static void StaticUnfusedChainKernel(int[] data, int takeCount, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        var unfused = StackLinqExtensions.AsStackEnumerable(data);
        results[0] = StackLinqExtensions.Sum(
            StackLinqExtensions.Take(
                StackLinqExtensions.Select(
                    StackLinqExtensions.Where(
                        unfused,
                        (x, idx) => idx % 2 == 0),
                    x => x + 1),
                takeCount));
        results[1] = S.ElementAtOrDefault(
            S.Select(
                S.Skip(S.AsStackEnumerable(data), 1),
                (x, idx) => x * 10 + idx),
            index: 1);
    }

    // 2d: Mixed extension + static chain, and non-interfering reordered named arguments
    [GpuKernel]
    public static void MixedAndNamedArgsKernel(int[] data, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        results[0] = StackLinqExtensions.Sum(
            S.Select(
                data.AsStackEnumerable().Where(x => x > 1),
                x => x * 3));
        results[1] = StackLinqExtensions.Where(
                predicate: x => x >= 3,
                source: StackLinqExtensions.AsStackEnumerable(data))
            .Sum();
        results[2] = S.Aggregate(
            func: (int acc, int x) => acc * 2 + x,
            seed: 1,
            source: S.Take(count: 3, source: S.Where(S.AsStackEnumerable(data), x => x > 0)));
    }

    [GpuKernel]
    public static void StackAllocSpanStaticKernel(int seed, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        Span<int> values = stackalloc int[4];
        values[0] = seed;
        values[1] = seed + 1;
        values[2] = seed + 2;
        values[3] = seed + 3;

        results[0] = StackLinqExtensions.Sum(
            StackLinqExtensions.Where(
                StackLinqExtensions.AsStackEnumerable(values),
                x => x % 2 == 0));
        var skipped = S.Skip(S.AsStackEnumerable(values), 1);
        results[1] = S.Count(skipped, x => x > seed) +
                     StackLinqExtensions.Sum(StackLinqExtensions.AsStackEnumerable(values.Slice(0, 0)));
    }

    // D1: Reduced extension calls whose receiver and arguments interfere are sequenced in C# order (receiver first)
    [GpuKernel]
    public static void InterferingExtensionReceiverKernel(int initial, int[] results)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        int value = initial;
        // Receiver 'value' is evaluated before 'value += 5': initial * 100 + (initial + 5)
        results[0] = value.WeightedPair(value += 5);

        Span<int> slots = stackalloc int[3];
        slots[0] = 10;
        slots[1] = 20;
        slots[2] = 30;
        int idx = 0;
        // Receiver 'ref slots[idx]' is bound to slots[0] before 'idx++' and 'idx * 10'
        slots[idx].AddWeighted(idx++, idx * 10);
        results[1] = slots[0] * 100 + slots[1] * 10 + idx;

        Span<int> sortBuf = stackalloc int[6];
        sortBuf[0] = 30;
        sortBuf[1] = 10;
        sortBuf[2] = 20;
        sortBuf[3] = 300;
        sortBuf[4] = 100;
        sortBuf[5] = 200;
        int offset = 0;
        // Receiver 'sortBuf.Slice(offset, 3)' (keys: [30,10,20]) is evaluated before '(offset += 3)' in items argument
        sortBuf.Slice(offset, 3).Sort(sortBuf.Slice(offset += 3, 3));
        results[2] = sortBuf[0] * 1000 + sortBuf[3];
    }
}
