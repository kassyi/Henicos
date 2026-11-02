#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for 1D/2D/3D launch shapes, multi-dimensional index linearization, grid-stride loops,
/// <c>Gpu.SingleThread</c>, cross-axis warp shuffles, multi-dimensional shared memory, and value-returning kernels
/// (Lane D1: <c>D1-1</c> through <c>D1-6</c>).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class LaunchShapeDifferentialTests : TranspilerTestBase
{
    private const int GridStrideMultiN = 1031;
    private const int GridStrideShortN = 19;

    // -------------------------------------------------------------------------
    // D1-1: 3D Index Linearization, Bijection & Signed Arithmetic
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_threeDimensionalIndexAndSignedness =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.ThreeDimensionalIndexAndSignednessKernel)}")
        {
            Shapes = LaunchShapes.FullMetamorphicMatrix(150),
            Inputs = static (_, s) => new DiffBuffers()
                .Out<long>("outGid", s.Rows, OutputPolicy.Exact)
                .Out<int>("outSignedMath", s.Rows, LaunchShapeDifferentialKernels.SignedMathColumns, OutputPolicy.Exact)
                .Out<int>("visitCount", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => LaunchShapeDifferentialKernels.LaunchThreeDimensionalIndexAndSignednessKernel(
                b.Get<long>("outGid"),
                b.Get<int>("outSignedMath"),
                b.Get<int>("visitCount"),
                s.Rows,
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                var outGid = b.Get<long>("outGid");
                var outSignedMath = b.Get<int>("outSignedMath");
                var visitCount = b.Get<int>("visitCount");

                for (int linearId = 0; linearId < s.ThreadCount; linearId++)
                {
                    var ctx = s.ContextOf(linearId);
                    long gid = LaunchShapeIndexMath.GlobalLinearGid(in ctx);
                    if (gid < s.Rows)
                    {
                        int idx = (int)gid;
                        outGid[idx] = gid;

                        int baseOffset = idx * 4;
                        outSignedMath[baseOffset + 0] =
                            (ctx.ThreadIdxX - ctx.BlockDimX)
                            + (ctx.ThreadIdxY - ctx.BlockDimY)
                            + (ctx.ThreadIdxZ - ctx.BlockDimZ);
                        outSignedMath[baseOffset + 1] =
                            ((ctx.BlockIdxX - ctx.GridDimX) * 3)
                            + ((ctx.BlockIdxY - ctx.GridDimY) * 5)
                            + ((ctx.BlockIdxZ - ctx.GridDimZ) * 7);
                        outSignedMath[baseOffset + 2] =
                            (-ctx.ThreadIdxX - 1) ^ (-ctx.BlockIdxX - 1) ^ (-ctx.BlockDimZ);
                        outSignedMath[baseOffset + 3] =
                            ((ctx.ThreadIdxX - ctx.ThreadIdxY) / 2)
                            + ((ctx.BlockIdxX - ctx.BlockIdxY) % 3)
                            + ((ctx.ThreadIdxZ - ctx.BlockIdxZ) >> 1);

                        visitCount[idx] += 1;
                    }
                }
            },
        };

    // -------------------------------------------------------------------------
    // D1-2: Gpu.GridStrideLoop 3D & Metamorphic Shape Invariance
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_gridStrideLoopMetamorphic =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.GridStrideLoopMetamorphicKernel)}")
        {
            Shapes = LaunchShapes.FullMetamorphicMatrix(64),
            Inputs = static (rng, _) => new DiffBuffers()
                .In("src", BoundedInt32(rng, GridStrideMultiN, -50_000, 50_000))
                .InScalar("bias", rng.NextBelow(2000) - 1000)
                .Out<int>("dstMulti", GridStrideMultiN, OutputPolicy.Exact)
                .Out<int>("dstShort", GridStrideShortN, OutputPolicy.Exact)
                .Out<int>("zeroGuard", 2, OutputPolicy.Exact),
            Launch = static (b, s) => LaunchShapeDifferentialKernels.LaunchGridStrideLoopMetamorphicKernel(
                b.Get<int>("src"),
                b.Get<int>("dstMulti"),
                b.Get<int>("dstShort"),
                b.Get<int>("zeroGuard"),
                GridStrideMultiN,
                GridStrideShortN,
                b.GetScalar<int>("bias"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, _) =>
            {
                var src = b.Get<int>("src");
                int bias = b.GetScalar<int>("bias");
                var dstMulti = b.Get<int>("dstMulti");
                var dstShort = b.Get<int>("dstShort");
                var zeroGuard = b.Get<int>("zeroGuard");

                for (int i = 0; i < GridStrideMultiN; i++)
                {
                    dstMulti[i] = (src[i] ^ (i * 0x45D9F3B)) + bias;
                }

                int state = bias ^ 0x13579BDF;
                for (int i = 0; i < GridStrideShortN; i++)
                {
                    dstShort[i] = src[i] + state + (i * 17);
                }

                zeroGuard[0] = 0;
                zeroGuard[1] = 0;
            },
        };

    // -------------------------------------------------------------------------
    // D1-3: Gpu.SingleThread 3D Origin & Early-Return Semantics (#693)
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_singleThreadMultiDimAndEarlyReturn =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.SingleThreadMultiDimAndEarlyReturnKernel)}")
        {
            Shapes =
            [
                ..LaunchShapes.StandardMatrix(96),
                ..LaunchShapes.AxisIsolated(96),
                ..LaunchShapes.CrossAxisWarp(96),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .InScalar("earlyExitLeader", rng.NextBool() ? 1 : 0)
                .Out<int>("header", 1, LaunchShapeDifferentialKernels.SingleThreadHeaderColumns, OutputPolicy.Exact)
                .Out<int>("perThreadOut", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => LaunchShapeDifferentialKernels.LaunchSingleThreadMultiDimAndEarlyReturnKernel(
                b.Get<int>("header"),
                b.Get<int>("perThreadOut"),
                s.Rows,
                b.GetScalar<int>("earlyExitLeader"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                int earlyExit = b.GetScalar<int>("earlyExitLeader");
                var header = b.Get<int>("header");
                var perThreadOut = b.Get<int>("perThreadOut");

                header[0] = 1;
                header[1] = 0;
                header[2] = earlyExit != 0 ? 111 : 222;

                for (int i = 0; i < s.Rows; i++)
                {
                    perThreadOut[i] = (i * 3) + 7;
                }
            },
        };

    // -------------------------------------------------------------------------
    // D1-4: Cross-Axis 2D/3D Warp Shuffle & Partial-Warp ActiveMask
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_crossAxisWarpShuffleAndActiveMask =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.CrossAxisWarpShuffleAndActiveMaskKernel)}")
        {
            Shapes =
            [
                ..LaunchShapes.CrossAxisWarp(150),
                ..LaunchShapes.NonMultipleOfWarp(150),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("srcInt", BoundedInt32(rng, s.Rows, -10_000, 10_000))
                .In("srcFloat", DiffValues.Single(rng, s.Rows))
                .In("srcUInt", DiffValues.Random(rng, s.Rows, static r => r.NextUInt32()))
                .Out<uint>("outMask", s.Rows, OutputPolicy.Exact)
                .Out<int>("outShflInt", s.Rows, LaunchShapeDifferentialKernels.CrossAxisShuffleIntColumns, OutputPolicy.Exact)
                .Out<float>("outShflFloat", s.Rows, OutputPolicy.Bitwise)
                .Out<uint>("outShflUInt", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => LaunchShapeDifferentialKernels.LaunchCrossAxisWarpShuffleAndActiveMaskKernel(
                b.Get<int>("srcInt"),
                b.Get<float>("srcFloat"),
                b.Get<uint>("srcUInt"),
                b.Get<uint>("outMask"),
                b.Get<int>("outShflInt"),
                b.Get<float>("outShflFloat"),
                b.Get<uint>("outShflUInt"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                var srcInt = b.Get<int>("srcInt");
                var srcFloat = b.Get<float>("srcFloat");
                var srcUInt = b.Get<uint>("srcUInt");
                var outMask = b.Get<uint>("outMask");
                var outShflInt = b.Get<int>("outShflInt");
                var outShflFloat = b.Get<float>("outShflFloat");
                var outShflUInt = b.Get<uint>("outShflUInt");

                int blockThreads = s.ThreadsPerBlock;
                int totalBlocks = checked((int)s.TotalBlocks);
                int rows = s.Rows;

                var blockInt = new int[blockThreads];
                var blockFloat = new float[blockThreads];
                var blockUInt = new uint[blockThreads];

                for (int bid = 0; bid < totalBlocks; bid++)
                {
                    int blockBaseGid = bid * blockThreads;
                    for (int tid = 0; tid < blockThreads; tid++)
                    {
                        int gid = blockBaseGid + tid;
                        blockInt[tid] = gid < rows ? srcInt[gid] : (tid + 1);
                        blockFloat[tid] = gid < rows ? srcFloat[gid] : (tid * 0.5f);
                        blockUInt[tid] = gid < rows ? srcUInt[gid] : (uint)(tid * 3 + 7);
                    }

                    for (int warpStart = 0; warpStart < blockThreads; warpStart += 32)
                    {
                        int activeLanes = Math.Min(32, blockThreads - warpStart);
                        uint expectedMask = LaunchShapeIndexMath.ExpectedConvergedActiveMask(warpStart, blockThreads);

                        for (int lane = 0; lane < activeLanes; lane++)
                        {
                            int tid = warpStart + lane;
                            int gid = blockBaseGid + tid;
                            if (gid >= rows)
                            {
                                continue;
                            }

                            int shfl0 = blockInt[warpStart];
                            int up1 = lane >= 1 ? blockInt[warpStart + lane - 1] : blockInt[tid];

                            int safeXor1 = ((lane ^ 1) < activeLanes) ? 1 : 0;
                            int xor1 = blockInt[warpStart + (lane ^ safeXor1)];

                            int seg4Target = (lane & ~3) + ((lane + 1) & 3);
                            int safeSeg4Lane = seg4Target < activeLanes ? (seg4Target & 3) : (lane & 3);
                            int shflSeg4 = blockInt[warpStart + (lane & ~3) + safeSeg4Lane];

                            int safeDown1 = (lane + 1 < activeLanes) ? 1 : 0;
                            float fDown1 = blockFloat[warpStart + lane + safeDown1];
                            uint uXor1 = blockUInt[warpStart + (lane ^ safeXor1)];

                            outMask[gid] = expectedMask;
                            int baseIdx = gid * 4;
                            outShflInt[baseIdx + 0] = shfl0;
                            outShflInt[baseIdx + 1] = up1;
                            outShflInt[baseIdx + 2] = xor1;
                            outShflInt[baseIdx + 3] = shflSeg4;
                            outShflFloat[gid] = fDown1;
                            outShflUInt[gid] = uXor1;
                        }
                    }
                }
            },
        };

    // -------------------------------------------------------------------------
    // D1-5: 2D/3D SharedMemory & Gpu.SyncThreads State-Machine Lowering
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_multiDimSharedMemoryAndBarrier =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.MultiDimSharedMemoryAndBarrierKernel)}")
        {
            Shapes =
            [
                ..LaunchShapes.StandardMatrix(128),
                ..LaunchShapes.CrossAxisWarp(128),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("src", BoundedInt32(rng, s.Rows, -1000, 1000))
                .Out<int>("outPermuted", s.Rows, OutputPolicy.Exact)
                .Out<int>("outBlockSum", s.Rows, OutputPolicy.Exact),
            Launch = static (b, s) => LaunchShapeDifferentialKernels.LaunchMultiDimSharedMemoryAndBarrierKernel(
                b.Get<int>("src"),
                b.Get<int>("outPermuted"),
                b.Get<int>("outBlockSum"),
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ,
                sharedMemBytes: (uint)(s.ThreadsPerBlock * sizeof(int))
            ),
            Reference = static (b, s) =>
            {
                var src = b.Get<int>("src");
                var outPermuted = b.Get<int>("outPermuted");
                var outBlockSum = b.Get<int>("outBlockSum");

                int blockThreads = s.ThreadsPerBlock;
                int totalBlocks = checked((int)s.TotalBlocks);
                int rows = s.Rows;

                var staticBuf = new int[blockThreads];
                var dynamicBuf = new int[blockThreads];
                var permuted = new int[blockThreads];

                for (int bid = 0; bid < totalBlocks; bid++)
                {
                    int blockBaseGid = bid * blockThreads;
                    for (int tid = 0; tid < blockThreads; tid++)
                    {
                        int gid = blockBaseGid + tid;
                        int val = gid < rows ? src[gid] : 0;
                        staticBuf[tid] = val;
                        dynamicBuf[tid] = val * 2;
                    }

                    int blockSum = 0;
                    for (int tid = 0; tid < blockThreads; tid++)
                    {
                        int partner = blockThreads - 1 - tid;
                        permuted[tid] = staticBuf[partner] + dynamicBuf[(tid + 1) % blockThreads];
                        blockSum += permuted[tid];
                    }

                    for (int tid = 0; tid < blockThreads; tid++)
                    {
                        int gid = blockBaseGid + tid;
                        if (gid < rows)
                        {
                            outPermuted[gid] = permuted[tid];
                            outBlockSum[gid] = blockSum;
                        }
                    }
                }
            },
        };

    // -------------------------------------------------------------------------
    // D1-6: Value-Returning Kernel (AutoResult) across 2D/3D Shapes
    // -------------------------------------------------------------------------
    private static readonly DifferentialCase s_autoResultThreeDimensional =
        new($"{nameof(LaunchShapeDifferentialKernels)}.{nameof(LaunchShapeDifferentialKernels.AutoResultThreeDimensionalKernel)}")
        {
            Shapes =
            [
                ..LaunchShapes.StandardMatrix(100),
                ..LaunchShapes.AxisIsolated(100),
                ..LaunchShapes.CrossAxisWarp(100),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("src", BoundedInt32(rng, s.Rows, -50_000, 50_000))
                .InScalar("salt", rng.NextInt32())
                .OutAutoResult<int>(s.ThreadCount, OutputPolicy.Exact),
            Launch = static (b, s) =>
            {
                var ret = LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                    b.Get<int>("src"),
                    b.GetScalar<int>("salt"),
                    gridDimX: s.GridX,
                    gridDimY: s.GridY,
                    gridDimZ: s.GridZ,
                    blockDimX: s.BlockX,
                    blockDimY: s.BlockY,
                    blockDimZ: s.BlockZ
                );
                b.SetAutoResult<int>(ret);
            },
            Reference = static (b, s) =>
            {
                var src = b.Get<int>("src");
                int salt = b.GetScalar<int>("salt");
                var dst = b.GetAutoResult<int>();

                for (int gid = 0; gid < s.ThreadCount; gid++)
                {
                    var ctx = s.ContextOf(gid);
                    int coordMix = (ctx.ThreadIdxX * 3)
                        + (ctx.ThreadIdxY * 5)
                        + (ctx.ThreadIdxZ * 7)
                        + (ctx.BlockIdxX * 11)
                        + (ctx.BlockIdxY * 13)
                        + (ctx.BlockIdxZ * 17);
                    int v = gid < src.Length ? src[gid] : coordMix;
                    dst[gid] = (v ^ salt) + coordMix;
                }
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ThreeDimensionalIndexAndSignedness_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_threeDimensionalIndexAndSignedness);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GridStrideLoopMetamorphic_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideLoopMetamorphic);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleThreadMultiDimAndEarlyReturn_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleThreadMultiDimAndEarlyReturn);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CrossAxisWarpShuffleAndActiveMask_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_crossAxisWarpShuffleAndActiveMask);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_MultiDimSharedMemoryAndBarrier_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_multiDimSharedMemoryAndBarrier);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AutoResultThreeDimensional_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_autoResultThreeDimensional);

    private static int[] BoundedInt32(DiffRandom rng, int count, int minInclusive, int maxExclusive)
    {
        int span = maxExclusive - minInclusive;
        return DiffValues.Random(rng, count, r => minInclusive + r.NextBelow(span));
    }
}
