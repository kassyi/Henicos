using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// Warp shuffles, votes and reductions in a using or fixed statement and in the scope of a using declaration, which the
// CPU state machine does not split (GSH0062); a vote in a local function (GSH0062); a vote and a shuffle in a
// Gpu.GridStrideLoop body, which the CPU lowering turns into a loop (no diagnostic)
public static unsafe partial class WarpExchangeInScopeKernels
{
    public struct Scope : IDisposable
    {
        public int Tag;

        public void Dispose()
        {
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UsingStatementKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int v = 0;
        using (Scope scope = new Scope { Tag = 1 })
        {
            v = Warp.ShuffleXorSync(0xFFFFFFFFu, tid + 10, 1) * scope.Tag;
        }

        output[tid] = v;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void UsingDeclarationKernel(uint[] output)
    {
        int tid = ThreadIndex.X;
        using Scope scope = new Scope { Tag = 1 };
        output[tid] = Warp.BallotSync(0xFFFFFFFFu, (tid & scope.Tag) != 0);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void FixedStatementKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        fixed (int* p = output)
        {
            p[tid] = Warp.ReduceAddSync(0xFFFFFFFFu, tid);
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void VoteInLocalFunctionKernel(uint[] output)
    {
        bool AnyOdd(int v) => Warp.AnySync(0xFFFFFFFFu, (v & 1) != 0);

        int tid = ThreadIndex.X;
        output[tid] = AnyOdd(tid) ? 1u : 0u;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Happy Path")]
    public static void GridStrideLoopKernel(uint[] output)
    {
        Gpu.GridStrideLoop(output.Length, i =>
        {
            output[i] = Warp.BallotSync(0xFFFFFFFFu, (i & 1) != 0) + (uint)Warp.ShuffleSync(0xFFFFFFFFu, i, 0);
        });
    }

    // A using statement and a fixed statement without a warp exchange are not reported
    [GpuKernel]
    [SkipSnapshotConsistency("Happy Path")]
    public static void ScopesWithoutExchangeKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int v = Warp.ShuffleXorSync(0xFFFFFFFFu, tid, 1);
        using (Scope scope = new Scope { Tag = 2 })
        {
            v *= scope.Tag;
        }

        fixed (int* p = output)
        {
            p[tid] = v;
        }
    }
}
