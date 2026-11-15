using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: lambdas converted to an expression tree; GSH0107, GSH0109 and GSH0003: implicit calls (a collection builder,
// a GetPinnableReference) that device code cannot make
public static unsafe partial class ImplicitCallFormKernels
{
    [GpuDeviceFunction]
    public static int Inc(int x) => x + 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ExpressionTreeKernel(int[] data)
    {
        Expression<Func<int>> call = () => Inc(41);
        Expression<Func<int, int>> add = x => x + 1;
        LambdaExpression natural = () => 2;
        Func<int> plain = () => Inc(1);
        data[0] = plain();
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnmarkedBuilderKernel(int[] data)
    {
        UnmarkedBag bag = [1, 2];
        data[0] = bag.First;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BclBuilderKernel(int[] data)
    {
        ImmutableArray<int> values = [1, 2];
        data[0] = values.Length;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarrierBuilderKernel(int[] data)
    {
        BarrierBag bag = [1, 2];
        data[0] = bag.First;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void InstancePinnableKernel(int[] data)
    {
        var holder = new InstancePinned { Data = data };
        fixed (int* p = holder)
        {
            p[0] = 1;
        }
    }
}

[CollectionBuilder(typeof(UnmarkedBagBuilder), nameof(UnmarkedBagBuilder.Create))]
public struct UnmarkedBag
{
    public int First;

    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

public static class UnmarkedBagBuilder
{
    public static UnmarkedBag Create(ReadOnlySpan<int> values) => new() { First = values[0] };
}

[CollectionBuilder(typeof(BarrierBagBuilder), nameof(BarrierBagBuilder.Create))]
public struct BarrierBag
{
    public int First;

    public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

public static partial class BarrierBagBuilder
{
    [GpuDeviceFunction]
    public static BarrierBag Create(ReadOnlySpan<int> values)
    {
        Gpu.SyncThreads();
        BarrierBag bag;
        bag.First = values[0];
        return bag;
    }
}

public struct InstancePinned
{
    public int[] Data;

    public ref int GetPinnableReference() => ref Data[0];
}
