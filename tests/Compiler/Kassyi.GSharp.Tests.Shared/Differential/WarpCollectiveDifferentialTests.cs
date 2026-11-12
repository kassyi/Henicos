#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the warp identity, the warp votes and reductions (#845) and warp shuffles in a
/// <see cref="Gpu.GridStrideLoop(int, Action{int})"/> body (#1091): the CPU paths give the CUDA result.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class WarpCollectiveDifferentialTests
{
    // One warp, a block of 40 threads in two dimensions (a last warp of 8 lanes), and three dimensions over two blocks
    private static readonly LaunchShape[] s_identityShapes =
    [
        LaunchShape.ForRows(64, blockX: 64),
        new LaunchShape(1, 1, 1, 8, 5, 1, 40),
        new LaunchShape(2, 1, 1, 4, 4, 3, 96),
    ];

    // Full warps over two blocks, and a block of 40 threads whose second warp has 8 lanes
    private static readonly LaunchShape[] s_voteShapes =
    [
        LaunchShape.ForRows(128, blockX: 64),
        new LaunchShape(2, 1, 1, 40, 1, 1, 80),
        new LaunchShape(1, 1, 1, 8, 5, 1, 40),
    ];

    internal static readonly DifferentialCase s_warpIdentity =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.WarpIdentityKernel)}")
        {
            Shapes = s_identityShapes,
            Seeds = [1],
            Inputs = static (_, s) => new DiffBuffers()
                .Out<int>("laneOut", s.Rows, ["beforeBarrier", "afterBarrier"], OutputPolicy.Exact)
                .Out<int>("warpOut", s.Rows, ["beforeBarrier", "afterBarrier"], OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchWarpIdentityKernel(
                b.Get<int>("laneOut"),
                b.Get<int>("warpOut"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                var laneOut = b.Get<int>("laneOut");
                var warpOut = b.Get<int>("warpOut");
                for (int gid = 0; gid < s.ThreadCount; gid++)
                {
                    int local = gid % s.ThreadsPerBlock;
                    laneOut[gid * 2] = laneOut[gid * 2 + 1] = local & 31;
                    warpOut[gid * 2] = warpOut[gid * 2 + 1] = local >> 5;
                }
            },
        };

    internal static readonly DifferentialCase s_warpVoteAndReduce =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.WarpVoteAndReduceKernel)}")
        {
            Shapes = s_voteShapes,
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.Rows))
                .Out<uint>("output", s.Rows, WarpCollectiveDifferentialKernels.VoteColumns, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchWarpVoteAndReduceKernel(
                b.Get<int>("input"),
                b.Get<uint>("output"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) => VoteReference(b.Get<int>("input"), b.Get<uint>("output"), s),
        };

    internal static readonly DifferentialCase s_voteInDeviceFunction =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.VoteInDeviceFunctionKernel)}")
        {
            Shapes = s_voteShapes,
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.Rows))
                .Out<uint>("output", s.Rows, ["oddLanes", "ballotNamed"], OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchVoteInDeviceFunctionKernel(
                b.Get<int>("input"),
                b.Get<uint>("output"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<uint>("output");
                for (int gid = 0; gid < s.ThreadCount; gid++)
                {
                    int local = gid % s.ThreadsPerBlock;
                    int lane = local & 31;
                    int lanes = Math.Min(32, s.ThreadsPerBlock - (local - lane));
                    uint odd = 0;
                    uint ballot = 0;
                    for (int l = 0; l < lanes; l++)
                    {
                        int x = input[gid - lane + l];
                        odd += (uint)(x & 1);
                        ballot |= (x & 4) != 0 ? 1u << l : 0u;
                    }

                    output[gid * 2] = odd;
                    output[gid * 2 + 1] = ballot;
                }
            },
        };

    internal static readonly DifferentialCase s_gridStrideBallot =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.GridStrideBallotKernel)}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows * 2))
                .Out<uint>("output", rows, WarpCollectiveDifferentialKernels.GridStrideColumns, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchGridStrideBallotKernel(
                b.Get<int>("input"),
                b.Get<uint>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<uint>("output");
                int totalThreads = output.Length / 2;
                for (int round = 0; round < 2; round++)
                {
                    for (int tid = 0; tid < totalThreads; tid++)
                    {
                        int warpBase = round * totalThreads + tid - (tid & 31);
                        uint ballot = 0;
                        for (int l = 0; l < 32; l++)
                        {
                            ballot |= (input[warpBase + l] & 1) != 0 ? 1u << l : 0u;
                        }

                        output[tid * 2 + round] = ballot;
                    }
                }
            },
        };

    internal static readonly DifferentialCase s_ballotCallerOutsideMask =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.BallotCallerOutsideMaskKernel)}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Seeds = [1],
            Inputs = static (_, s) => new DiffBuffers()
                .Out<uint>("output", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchBallotCallerOutsideMaskKernel(
                b.Get<uint>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            // Undefined on CUDA, which does not check it
            SkippedPaths = new System.Collections.Generic.Dictionary<DiffPath, string>
            {
                [DiffPath.Cuda] = "A mask without the caller's lane is undefined on CUDA.",
            },
            ExpectedException = static (_, _) => ExceptionPolicy.For<InvalidOperationException>(
                DiffPath.Managed | DiffPath.Native,
                messageContains: "Warp.BallotSync"),
        };

    internal static readonly DifferentialCase s_reduceCallerOutsideMask =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.ReduceCallerOutsideMaskKernel)}")
        {
            Shape = LaunchShape.ForRows(40, blockX: 40),
            Seeds = [1],
            Inputs = static (_, s) => new DiffBuffers()
                .Out<int>("output", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchReduceCallerOutsideMaskKernel(
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            SkippedPaths = new System.Collections.Generic.Dictionary<DiffPath, string>
            {
                [DiffPath.Cuda] = "A mask without the caller's lane is undefined on CUDA.",
            },
            ExpectedException = static (_, _) => ExceptionPolicy.For<InvalidOperationException>(
                DiffPath.Managed | DiffPath.Native,
                messageContains: "Warp.ReduceMinSync"),
        };

    internal static readonly DifferentialCase s_gridStrideShuffleInDeviceFunction =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.GridStrideShuffleInDeviceFunctionKernel)}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Seeds = [1],
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows * 2))
                .Out<int>("output", rows, WarpCollectiveDifferentialKernels.GridStrideColumns, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchGridStrideShuffleInDeviceFunctionKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
                GridStrideReference(b.Get<int>("input"), b.Get<int>("output"), static lane => lane + 1 < 32 ? 1 : 0),
        };

    internal static readonly DifferentialCase s_gridStrideShuffleWithContext =
        new($"{nameof(WarpCollectiveDifferentialKernels)}.{nameof(WarpCollectiveDifferentialKernels.GridStrideShuffleWithContextKernel)}")
        {
            Shape = LaunchShape.ForRows(96, blockX: 32),
            Seeds = [1],
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows * 2))
                .Out<int>("output", rows, WarpCollectiveDifferentialKernels.GridStrideColumns, OutputPolicy.Exact),
            Launch = static (b, s) => WarpCollectiveDifferentialKernels.LaunchGridStrideShuffleWithContextKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) =>
                GridStrideReference(b.Get<int>("input"), b.Get<int>("output"), static lane => lane > 0 ? -1 : 0),
        };

    // Each thread runs rounds 0 and 1 of the loop over 2 * totalThreads indices; offset gives the lane read relative to
    // the caller's
    private static void GridStrideReference(int[] input, int[] output, Func<int, int> offset)
    {
        int totalThreads = output.Length / 2;
        for (int round = 0; round < 2; round++)
        {
            for (int tid = 0; tid < totalThreads; tid++)
            {
                output[tid * 2 + round] = input[round * totalThreads + tid + offset(tid & 31)];
            }
        }
    }

    // The lanes of each warp that exist in the block take part; masks name lanes of the caller's warp
    private static void VoteReference(int[] input, uint[] output, LaunchShape s)
    {
        int cols = WarpCollectiveDifferentialKernels.VoteColumns.Length;
        for (int gid = 0; gid < s.ThreadCount; gid++)
        {
            int local = gid % s.ThreadsPerBlock;
            int lane = local & 31;
            int warpBase = gid - lane;
            int lanes = Math.Min(32, s.ThreadsPerBlock - (local - lane));
            uint existing = lanes == 32 ? 0xFFFFFFFFu : (1u << lanes) - 1u;
            uint halfMask = lane < 16 ? 0x0000FFFFu : 0xFFFF0000u;
            int odd = (local >> 5) & 1;

            uint Ballot(uint mask, Func<int, bool> predicate)
            {
                uint result = 0;
                for (int l = 0; l < 32; l++)
                {
                    if ((existing & mask & (1u << l)) != 0 && predicate(input[warpBase + l]))
                    {
                        result |= 1u << l;
                    }
                }

                return result;
            }

            T Reduce<T>(uint mask, Func<int, T> value, Func<T, T, T> combine)
            {
                bool first = true;
                T result = default!; // Assigned from the caller's own lane, which every mask here contains
                for (int l = 0; l < 32; l++)
                {
                    if ((existing & mask & (1u << l)) != 0)
                    {
                        T x = value(input[warpBase + l]);
                        result = first ? x : combine(result, x);
                        first = false;
                    }
                }

                return result;
            }

            bool AllPredicate(int x) => (x & 1) == 0 || odd == 0;
            bool AnyPredicate(int x) => (x & 15) == 0 && odd == 0;
            uint participants = existing & 0xFFFFFFFFu;
            int o = gid * cols;
            output[o + 0] = Ballot(0xFFFFFFFFu, static x => (x & 1) != 0);
            output[o + 1] = Ballot(0xFFFFFFFFu, AllPredicate) == participants ? 1u : 0u;
            output[o + 2] = Ballot(0xFFFFFFFFu, AnyPredicate) != 0 ? 1u : 0u;
            output[o + 3] = Ballot(halfMask, static x => (x & 2) != 0);
            output[o + 4] = Ballot(halfMask, AllPredicate) == (existing & halfMask) ? 1u : 0u;
            output[o + 5] = Ballot(halfMask, AnyPredicate) != 0 ? 1u : 0u;
            output[o + 6] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x, static (a, c) => unchecked(a + c)));
            output[o + 7] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x, Math.Min));
            output[o + 8] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x, Math.Max));
            output[o + 9] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x | 0x10, static (a, c) => a & c));
            output[o + 10] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x & 0x0F0F0F0F, static (a, c) => a | c));
            output[o + 11] = unchecked((uint)Reduce(0xFFFFFFFFu, static x => x, static (a, c) => a ^ c));
            output[o + 12] = Reduce(0xFFFFFFFFu, static x => unchecked((uint)x), static (a, c) => unchecked(a + c));
            output[o + 13] = Reduce(0xFFFFFFFFu, static x => unchecked((uint)x), Math.Min);
            output[o + 14] = Reduce(0xFFFFFFFFu, static x => unchecked((uint)x), Math.Max);
            output[o + 15] = unchecked((uint)Reduce(halfMask, static x => x, static (a, c) => unchecked(a + c)));
            output[o + 16] = Reduce(halfMask, static x => unchecked((uint)x), Math.Min);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpCollective_WarpIdentity_IsTheLinearThreadOfTheBlockOnEveryPath() =>
        DifferentialRunner.Run(s_warpIdentity);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void WarpCollective_VotesAndReductions_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_warpVoteAndReduce);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpCollective_ReductionInDeviceFnAndNamedBallot_MatchTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_voteInDeviceFunction);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpCollective_GridStrideLoopBallot_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideBallot);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void WarpCollective_BallotWithoutCallerInMask_ThrowsOnTheCpuPaths() =>
        DifferentialRunner.Run(s_ballotCallerOutsideMask);

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void WarpCollective_ReductionWithoutCallerInMask_ThrowsOnTheCpuPaths() =>
        DifferentialRunner.Run(s_reduceCallerOutsideMask);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpCollective_GridStrideLoopShuffleInDeviceFn_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideShuffleInDeviceFunction);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WarpCollective_GridStrideLoopShuffleWithContext_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideShuffleWithContext);
}
