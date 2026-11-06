using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// A warp shuffle where the CPU state machine does not split (GSH0062), and where it does (no diagnostic)
public static partial class WarpShuffleInUnsplitContextKernels
{
    public sealed class Box
    {
        public int Value;

        public int Add(int other) => Value + other;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LocalFunctionKernel(int[] data)
    {
        int Reduce(int v)
        {
            for (uint offset = 16; offset > 0; offset >>= 1)
            {
                v += Warp.ShuffleDownSync(0xFFFFFFFFu, v, offset);
            }

            return v;
        }

        int tid = ThreadIndex.X;
        data[tid] = Reduce(data[tid]);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void LambdaKernel(int[] data)
    {
        Func<int, int> broadcast = value => Warp.ShuffleSync(0xFFFFFFFFu, value, 0);
        data[ThreadIndex.X] = broadcast(data[ThreadIndex.X]);
    }

    // Not reported yet: the CPU paths do not split a Gpu.GridStrideLoop body either, but the Stdlib reduces there
    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void GridStrideLoopKernel(int[] data)
    {
        Gpu.GridStrideLoop(data.Length, i =>
        {
            data[i] += Warp.ShuffleXorSync(0xFFFFFFFFu, data[i], 1);
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void SwitchArmKernel(int[] data, int mode)
    {
        int tid = ThreadIndex.X;
        data[tid] = mode switch
        {
            0 => Warp.ShuffleDownSync(0xFFFFFFFFu, data[tid + 32], 1),
            _ => data[tid],
        };
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void WhenClauseKernel(int[] data, int mode)
    {
        int tid = ThreadIndex.X;
        switch (mode)
        {
            case 0 when Gpu.MatchAnySync(0xFFFFFFFFu, data[tid]) != 0u:
                data[tid] = 1;
                break;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ConditionalAccessKernel(int[] data, Box? box)
    {
        int tid = ThreadIndex.X;
        data[tid] = box?.Add(Warp.ShuffleUpSync(0xFFFFFFFFu, data[tid], 1)) ?? 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void CoalesceKernel(int[] data, int mode)
    {
        int tid = ThreadIndex.X;
        int? cached = mode == 0 ? null : data[tid];
        data[tid] = cached ?? Warp.ShuffleSync(0xFFFFFFFFu, data[tid], 0);
    }

    // The places the CPU state machine splits: a statement, an operand of '?:' or '&&', a Gpu.ForUnroll body
    [GpuKernel]
    [SkipSnapshotConsistency("Happy Path")]
    public static void SplitContextsKernel(int[] data, int mode)
    {
        int tid = ThreadIndex.X;
        int v = Warp.ShuffleSync(0xFFFFFFFFu, data[tid], 0);
        v += mode == 0 ? Warp.ShuffleDownSync(0xFFFFFFFFu, v, 1) : v;
        bool any = mode == 1 && Gpu.MatchAnySync(0xFFFFFFFFu, v) != 0u;
        Gpu.ForUnroll(0, 2, k =>
        {
            v += Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1 << k);
        });
        data[tid] = any ? v : -v;
    }
}
