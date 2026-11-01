#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<see cref="SharedMemory{T}"/>, <see cref="Gpu.SyncThreads"/>, and
/// <see cref="Warp"/> shuffle intrinsics).
/// </summary>
public class M1DiffWarpTests
{
    private static readonly DifferentialCase s_sharedMemoryBlockReductionAndScan =
        new("M1DiffWarpKernels.SharedMemoryBlockReductionAndScanKernel")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.SharedMemoryColumns, OutputPolicy.Exact),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchSharedMemoryBlockReductionAndScanKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                const int blockSize = 64;
                int length = input.Length;
                int cols = M1DiffWarpKernels.SharedMemoryColumns.Length;

                for (int blockStart = 0; blockStart < length; blockStart += blockSize)
                {
                    Span<int> vals = stackalloc int[blockSize];
                    Span<long> wide = stackalloc long[blockSize];
                    int blockSum = 0;
                    for (int tid = 0; tid < blockSize; tid++)
                    {
                        int gid = blockStart + tid;
                        int v = gid < length ? (input[gid] & 0x3FFF) - 0x1FFF : 0;
                        vals[tid] = v;
                        wide[tid] = (long)v * 3L + tid;
                        blockSum += v;
                    }

                    int runningPrefix = 0;
                    for (int tid = 0; tid < blockSize; tid++)
                    {
                        int gid = blockStart + tid;
                        int exclusiveScan = runningPrefix;
                        runningPrefix += vals[tid];
                        int inclusiveScan = runningPrefix;

                        int reversed = vals[(blockSize - 1) - tid];
                        int nextNeighbor = vals[(tid + 1) & (blockSize - 1)];
                        int prevNeighbor = vals[(tid + (blockSize - 1)) & (blockSize - 1)];
                        int haloCombined = reversed + (nextNeighbor - prevNeighbor);

                        long widePair = wide[tid] + wide[tid ^ 1];
                        int wideFolded = (int)widePair ^ (int)(widePair >> 32);

                        if (gid < length)
                        {
                            int o = gid * cols;
                            output[o + 0] = blockSum;
                            output[o + 1] = inclusiveScan;
                            output[o + 2] = exclusiveScan;
                            output[o + 3] = haloCombined;
                            output[o + 4] = wideFolded;
                        }
                    }
                }
            },
        };

    private static readonly DifferentialCase s_warpShuffleFullWidth =
        new("M1DiffWarpKernels.WarpShuffleFullWidthKernel")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.FullWarpShuffleColumns, OutputPolicy.Exact),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchWarpShuffleFullWidthKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                int length = input.Length;
                int cols = M1DiffWarpKernels.FullWarpShuffleColumns.Length;

                for (int warpStart = 0; warpStart < length; warpStart += 32)
                {
                    Span<int> v = stackalloc int[32];
                    int warpSum = 0;
                    for (int lane = 0; lane < 32; lane++)
                    {
                        int tid = warpStart + lane;
                        int val = tid < length ? (input[tid] & 0xFFFF) - 0x7FFF : 0;
                        v[lane] = val;
                        warpSum += val;
                    }

                    int prefix = 0;
                    for (int lane = 0; lane < 32; lane++)
                    {
                        prefix += v[lane];
                        int tid = warpStart + lane;
                        if (tid >= length)
                        {
                            continue;
                        }

                        int o = tid * cols;
                        output[o + 0] = v[0];
                        output[o + 1] = v[19];
                        output[o + 2] = v[37 & 31];
                        output[o + 3] = lane + 1 < 32 ? v[lane + 1] : v[lane];
                        output[o + 4] = lane + 7 < 32 ? v[lane + 7] : v[lane];
                        output[o + 5] = lane >= 1 ? v[lane - 1] : v[lane];
                        output[o + 6] = lane >= 9 ? v[lane - 9] : v[lane];
                        output[o + 7] = v[lane ^ 1];
                        output[o + 8] = v[lane ^ 13];
                        output[o + 9] = warpSum;
                        output[o + 10] = prefix;
                    }
                }
            },
        };

    private static readonly DifferentialCase s_warpShuffleSubWarpWidths =
        new("M1DiffWarpKernels.WarpShuffleSubWarpWidthsKernel")
        {
            Shape = LaunchShape.ForRows(64, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.SubWarpShuffleColumns, OutputPolicy.Exact),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchWarpShuffleSubWarpWidthsKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                int length = input.Length;
                int cols = M1DiffWarpKernels.SubWarpShuffleColumns.Length;

                for (int warpStart = 0; warpStart < length; warpStart += 32)
                {
                    for (int lane = 0; lane < 32; lane++)
                    {
                        int tid = warpStart + lane;
                        if (tid >= length)
                        {
                            continue;
                        }

                        int seg8Base = lane & ~7;
                        int seg8Lane = lane & 7;
                        int seg16Base = lane & ~15;
                        int seg16Lane = lane & 15;
                        int seg4Base = lane & ~3;

                        int idx3W8 = input[warpStart + seg8Base + 3];
                        int idxWrap13W8 = input[warpStart + seg8Base + (13 & 7)];
                        int down3W8 = seg8Lane + 3 < 8 ? input[warpStart + lane + 3] : input[tid];
                        int up2W8 = seg8Lane >= 2 ? input[warpStart + lane - 2] : input[tid];
                        int xor3W8 = input[warpStart + (lane ^ 3)];
                        int xor8Src = lane ^ 8;
                        int xorCross8W8 = xor8Src <= (lane | 7) ? input[warpStart + xor8Src] : input[tid];
                        int down5W16 = seg16Lane + 5 < 16 ? input[warpStart + seg16Base + seg16Lane + 5] : input[tid];
                        int up5W16 = seg16Lane >= 5 ? input[warpStart + seg16Base + seg16Lane - 5] : input[tid];
                        int idx2W4 = input[warpStart + seg4Base + 2];
                        int down0W1 = input[tid];

                        int o = tid * cols;
                        output[o + 0] = idx3W8;
                        output[o + 1] = idxWrap13W8;
                        output[o + 2] = down3W8;
                        output[o + 3] = up2W8;
                        output[o + 4] = xor3W8;
                        output[o + 5] = xorCross8W8;
                        output[o + 6] = down5W16;
                        output[o + 7] = up5W16;
                        output[o + 8] = idx2W4;
                        output[o + 9] = down0W1;
                    }
                }
            },
        };

    private static readonly DifferentialCase s_hybridWarpAndSharedMemoryBlockReduce =
        new("M1DiffWarpKernels.HybridWarpAndSharedMemoryBlockReduceKernel")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.HybridReduceColumns, OutputPolicy.Exact)
                .Out<float>("floatOutput", rows, M1DiffWarpKernels.HybridFloatShuffleColumns, OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchHybridWarpAndSharedMemoryBlockReduceKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                b.Get<float>("floatOutput"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                var floatOutput = b.Get<float>("floatOutput");
                const int blockSize = 64;
                int length = input.Length;
                int cols = M1DiffWarpKernels.HybridReduceColumns.Length;
                int floatCols = M1DiffWarpKernels.HybridFloatShuffleColumns.Length;

                for (int blockStart = 0; blockStart < length; blockStart += blockSize)
                {
                    Span<int> vals = stackalloc int[blockSize];
                    Span<int> warpSums = stackalloc int[2];

                    for (int tid = 0; tid < blockSize; tid++)
                    {
                        int gid = blockStart + tid;
                        int v = gid < length ? (input[gid] & 0x3FFF) - 0x1FFF : 0;
                        vals[tid] = v;
                        warpSums[tid >> 5] += v;
                    }

                    int blockTotal = warpSums[0] + warpSums[1];

                    for (int tid = 0; tid < blockSize; tid++)
                    {
                        int gid = blockStart + tid;
                        if (gid >= length)
                        {
                            continue;
                        }

                        int warp = tid >> 5;
                        int lane = tid & 31;
                        int warpBase = warp << 5;

                        int downLane = lane + 3 < 32 ? lane + 3 : lane;
                        int upLane = lane >= 2 ? lane - 2 : lane;

                        float fDown = (vals[warpBase + downLane] * 0.25f) + 1.5f;
                        float fUp = (vals[warpBase + upLane] * 0.25f) + 1.5f;

                        int o = gid * cols;
                        output[o + 0] = blockTotal;
                        output[o + 1] = warpSums[warp ^ 1];

                        int fo = gid * floatCols;
                        floatOutput[fo + 0] = fDown;
                        floatOutput[fo + 1] = fUp;
                    }
                }
            },
        };

    private static readonly DifferentialCase s_subWarpConditionalShuffle =
        new("M1DiffWarpKernels.SubWarpConditionalShuffleKernel")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.SubWarpConditionalShuffleColumns, OutputPolicy.Exact),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchSubWarpConditionalShuffleKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                int cols = M1DiffWarpKernels.SubWarpConditionalShuffleColumns.Length;

                Span<int> aInit = stackalloc int[32];
                Span<int> bInit = stackalloc int[32];
                for (int lane = 0; lane < 32; lane++)
                {
                    int baseVal = (input[lane] & 0xFF) + 1;
                    aInit[lane] = baseVal * 10 + lane;
                    bInit[lane] = baseVal * 1000 + lane;
                }

                for (int lane = 0; lane < 32; lane++)
                {
                    int a = lane < 16
                        ? (lane + 1 < 16 ? aInit[lane + 1] : aInit[lane])
                        : aInit[lane];
                    int bVal = bInit[lane ^ 16];

                    int o = lane * cols;
                    output[o + 0] = a;
                    output[o + 1] = bVal;
                }
            },
        };

    private static readonly DifferentialCase s_genericStructStaticSharedMemory =
        new("M1DiffWarpKernels.GenericStructStaticSharedMemoryKernel")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, rows))
                .Out<int>("output", rows, M1DiffWarpKernels.GenericStructSharedColumns, OutputPolicy.Exact),
            Launch = static (b, s) => M1DiffWarpKernels.LaunchGenericStructStaticSharedMemoryKernel(
                b.Get<int>("input"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static b =>
            {
                var input = b.Get<int>("input");
                var output = b.Get<int>("output");
                int cols = M1DiffWarpKernels.GenericStructSharedColumns.Length;

                Span<int> sharedInt = stackalloc int[32];
                Span<int> sharedUint = stackalloc int[32];
                for (int lane = 0; lane < 32; lane++)
                {
                    int v = (input[lane] & 0xFF) + 1;
                    sharedInt[lane] = v * 10 + lane;
                    sharedUint[lane] = v * 1000 + lane;
                }

                for (int lane = 0; lane < 32; lane++)
                {
                    int mirror = 31 - lane;
                    int o = lane * cols;
                    output[o + 0] = sharedInt[mirror];
                    output[o + 1] = sharedUint[mirror];
                }
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SharedMemoryBlockReductionAndScan_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_sharedMemoryBlockReductionAndScan);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_WarpShuffleFullWidth_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_warpShuffleFullWidth);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_WarpShuffleSubWarpWidths_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_warpShuffleSubWarpWidths);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HybridWarpAndSharedMemoryBlockReduce_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_hybridWarpAndSharedMemoryBlockReduce);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SubWarpConditionalShuffleBeforeFullWarpShuffle_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_subWarpConditionalShuffle);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_GenericStructStaticSharedMemory_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_genericStructStaticSharedMemory);
}
