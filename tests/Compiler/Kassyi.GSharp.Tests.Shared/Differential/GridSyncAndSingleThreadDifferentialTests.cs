#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="Gpu.SyncGrid()"/>, <see cref="Gpu.SyncGrid(Action)"/>,
/// <see cref="Gpu.SingleThread(Action)"/>, cooperative multi-phase barrier + atomic workflows, and
/// Stage 4 compiler/analyzer diagnostic verification (Lane D4 Stages 3–4).
/// </summary>
public class GridSyncAndSingleThreadDifferentialTests
{
    private static readonly DifferentialCase s_cooperativeMultiPhaseReduceNormalize =
        new($"{nameof(GridSyncAndSingleThreadDifferentialKernels)}.{nameof(GridSyncAndSingleThreadDifferentialKernels.CooperativeMultiPhaseReduceNormalizeKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(1u, 64u),
                LaunchShapes.Grid1D(2u, 64u),
                LaunchShapes.Grid1D(4u, 64u),
                new LaunchShape(2u, 2u, 1u, 16u, 4u, 1u, 256),
            ],
            Inputs = static (rng, s) =>
            {
                int totalThreads = s.ThreadCount;
                int numBlocks = checked((int)s.TotalBlocks);
                return new DiffBuffers()
                    .In("input", DiffValues.Int32(rng, totalThreads), OutputPolicy.Exact, readOnly: true)
                    .In("blockSums", new int[numBlocks], OutputPolicy.Exact)
                    .In("gridStats", [0, 0, 0], OutputPolicy.Exact)
                    .Out<int>("output", totalThreads, GridSyncAndSingleThreadDifferentialKernels.MultiPhaseReduceColumns, OutputPolicy.Exact);
            },
            Launch = static (b, s) => GridSyncAndSingleThreadDifferentialKernels.LaunchCooperativeMultiPhaseReduceNormalizeKernel(
                b.Get<int>("input"),
                b.Get<int>("blockSums"),
                b.Get<int>("gridStats"),
                b.Get<int>("output"),
                dynBlockCap: s.ThreadsPerBlock,
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
                var input = b.Get<int>("input");
                var blockSums = b.Get<int>("blockSums");
                var gridStats = b.Get<int>("gridStats");
                var output = b.Get<int>("output");

                int blockSize = s.ThreadsPerBlock;
                int numBlocks = checked((int)s.TotalBlocks);

                int gridTotal = 0;
                int maxBlockSum = int.MinValue;
                for (int blockId = 0; blockId < numBlocks; blockId++)
                {
                    int bSum = 0;
                    int baseGid = blockId * blockSize;
                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int v = (input[baseGid + localTid] & 0x7FF) - 0x3FF;
                        bSum += v;
                    }

                    blockSums[blockId] = bSum;
                    gridTotal += bSum;
                    if (bSum > maxBlockSum)
                    {
                        maxBlockSum = bSum;
                    }
                }

                gridStats[0] = gridTotal;
                gridStats[1] = maxBlockSum;
                gridStats[2] = gridTotal;

                for (int blockId = 0; blockId < numBlocks; blockId++)
                {
                    int ownBlockSum = blockSums[blockId];
                    int nextBlockSum = blockSums[(blockId + 1) % numBlocks];
                    int baseGid = blockId * blockSize;
                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = baseGid + localTid;
                        int v = (input[gid] & 0x7FF) - 0x3FF;
                        int o = gid * 4;
                        output[o + 0] = ownBlockSum;
                        output[o + 1] = nextBlockSum;
                        output[o + 2] = gridTotal;
                        output[o + 3] = (v * numBlocks) + (maxBlockSum - ownBlockSum);
                    }
                }
            },
        };

    private static readonly DifferentialCase s_cooperativePingPongStencilAndXorSwap =
        new($"{nameof(GridSyncAndSingleThreadDifferentialKernels)}.{nameof(GridSyncAndSingleThreadDifferentialKernels.CooperativePingPongStencilAndXorSwapKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(2u, 32u),
                LaunchShapes.Grid1D(4u, 32u),
            ],
            Inputs = static (rng, s) =>
            {
                int total = s.ThreadCount;
                var state = new int[total];
                var raw = DiffValues.Int32(rng, total);
                for (int i = 0; i < total; i++)
                {
                    state[i] = (raw[i] & 0xFFFF) - 0x7FFF;
                }

                return new DiffBuffers()
                    .In("state", state, OutputPolicy.Exact)
                    .In("scratch", new int[total], OutputPolicy.Exact);
            },
            Launch = static (b, s) => GridSyncAndSingleThreadDifferentialKernels.LaunchCooperativePingPongStencilAndXorSwapKernel(
                b.Get<int>("state"),
                b.Get<int>("scratch"),
                steps: 3,
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var state = b.Get<int>("state");
                var scratch = b.Get<int>("scratch");
                int total = s.ThreadCount;
                const int steps = 3;

                for (int step = 0; step < steps; step++)
                {
                    for (int gid = 0; gid < total; gid++)
                    {
                        int prev = (gid + total - 1) % total;
                        int next = (gid + 1) % total;
                        int oppositeBlockPeer = (gid + (total >> 1)) % total;

                        int selfVal = state[gid];
                        int haloDelta = (state[prev] - state[next]) ^ (state[oppositeBlockPeer] & 0xFF);
                        scratch[gid] = selfVal + haloDelta + (step * 7);
                    }

                    for (int gid = 0; gid < total; gid++)
                    {
                        int partner = gid ^ 1;
                        int a = scratch[gid];
                        int bVal = scratch[partner];
                        state[gid] = (gid & 1) == 0
                            ? a + ((bVal & 0x3F) - 0x1F)
                            : bVal - ((a & 0x3F) - 0x1F);
                    }
                }
            },
        };

    private static readonly DifferentialCase s_cooperativeSyncGridBodyControlFlowAndDeviceFn =
        new($"{nameof(GridSyncAndSingleThreadDifferentialKernels)}.{nameof(GridSyncAndSingleThreadDifferentialKernels.CooperativeSyncGridBodyControlFlowAndDeviceFnKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(2u, 32u),
                LaunchShapes.Grid1D(4u, 32u),
            ],
            Inputs = static (rng, s) =>
            {
                int total = s.ThreadCount;
                return new DiffBuffers()
                    .In("input", DiffValues.Int32(rng, total), OutputPolicy.Exact, readOnly: true)
                    .In("phaseBuf", new int[total], OutputPolicy.Exact)
                    .In("scratch", new int[total], OutputPolicy.Exact)
                    .Out<int>("output", total, OutputPolicy.Exact);
            },
            Launch = static (b, s) => GridSyncAndSingleThreadDifferentialKernels.LaunchCooperativeSyncGridBodyControlFlowAndDeviceFnKernel(
                b.Get<int>("input"),
                b.Get<int>("phaseBuf"),
                b.Get<int>("scratch"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<int>("input");
                var phaseBuf = b.Get<int>("phaseBuf");
                var scratch = b.Get<int>("scratch");
                var output = b.Get<int>("output");
                int total = s.ThreadCount;

                // Phase 1
                for (int gid = 0; gid < total; gid++)
                {
                    if ((gid & 1) == 0)
                    {
                        phaseBuf[gid] = (input[gid] & 0xFFF) + 100;
                    }
                    else
                    {
                        int oddBase = (input[gid] & 0xFFF) + 200;
                        phaseBuf[gid] = (oddBase * 3) - 7;
                    }
                }

                // Phase 2
                for (int gid = 0; gid < total; gid++)
                {
                    int nextNeighbor = phaseBuf[(gid + 1) % total];
                    scratch[gid] = (gid & 3) switch
                    {
                        0 => nextNeighbor + 10,
                        1 => nextNeighbor + 20 + 5,
                        2 => nextNeighbor + 30,
                        _ => nextNeighbor + 40 + 5,
                    };
                }

                // Phase 3
                for (int round = 0; round < 2; round++)
                {
                    for (int gid = 0; gid < total; gid++)
                    {
                        int opposite = scratch[(gid + (total >> 1)) % total];
                        phaseBuf[gid] = phaseBuf[gid] + opposite + (round * 11);
                    }
                }

                // Phase 4: OuterGridStep -> InnerGridStep
                for (int gid = 0; gid < total; gid++)
                {
                    int partner = (gid + 3) % total;
                    scratch[gid] = phaseBuf[gid] ^ (phaseBuf[partner] + 19);
                }

                for (int gid = 0; gid < total; gid++)
                {
                    int prev = (gid + total - 1) % total;
                    output[gid] = scratch[gid] + scratch[prev];
                }
            },
        };

    private static readonly DifferentialCase s_singleThreadMultiDimAndReturn =
        new($"{nameof(GridSyncAndSingleThreadDifferentialKernels)}.{nameof(GridSyncAndSingleThreadDifferentialKernels.SingleThreadMultiDimAndReturnKernel)}")
        {
            Shapes =
            [
                new LaunchShape(2u, 2u, 2u, 2u, 2u, 2u, 64),
                new LaunchShape(1u, 3u, 2u, 4u, 2u, 2u, 96),
                LaunchShapes.Grid1D(2u, 32u),
            ],
            Inputs = static (_, s) => new DiffBuffers()
                .In("counters", [0, 0, 0, 0], OutputPolicy.Exact)
                .Out<int>("output", s.ThreadCount, GridSyncAndSingleThreadDifferentialKernels.SingleThreadMultiDimColumns, OutputPolicy.Exact),
            Launch = static (b, s) => GridSyncAndSingleThreadDifferentialKernels.LaunchSingleThreadMultiDimAndReturnKernel(
                b.Get<int>("counters"),
                b.Get<int>("output"),
                seedArg: 23,
                gridDimX: s.GridX,
                gridDimY: s.GridY,
                gridDimZ: s.GridZ,
                blockDimX: s.BlockX,
                blockDimY: s.BlockY,
                blockDimZ: s.BlockZ
            ),
            Reference = static (b, s) =>
            {
                var counters = b.Get<int>("counters");
                var output = b.Get<int>("output");
                const int seedArg = 23;

                counters[0] = 1;
                counters[1] = (seedArg * 17) + 0x1234;
                counters[2] = 1;
                counters[3] = (seedArg * 10) + 7;

                int blockSize = s.ThreadsPerBlock;
                int numBlocks = checked((int)s.TotalBlocks);
                for (int blockId = 0; blockId < numBlocks; blockId++)
                {
                    int broadcast = (blockId + 1) * 100;
                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = (blockId * blockSize) + localTid;
                        int o = gid * 3;
                        output[o + 0] = gid;
                        output[o + 1] = broadcast;
                        output[o + 2] = seedArg + localTid + 1;
                    }
                }
            },
        };

    private static readonly DifferentialCase s_singleThreadCooperativeBarrier =
        new($"{nameof(GridSyncAndSingleThreadDifferentialKernels)}.{nameof(GridSyncAndSingleThreadDifferentialKernels.SingleThreadCooperativeBarrierKernel)}")
        {
            Shapes =
            [
                LaunchShapes.Grid1D(2u, 32u),
                LaunchShapes.Grid1D(4u, 32u),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.ThreadCount), OutputPolicy.Exact, readOnly: true)
                .In("globalState", [-1, -1, -1], OutputPolicy.Exact)
                .Out<int>("output", s.ThreadCount, OutputPolicy.Exact),
            Launch = static (b, s) => GridSyncAndSingleThreadDifferentialKernels.LaunchSingleThreadCooperativeBarrierKernel(
                b.Get<int>("input"),
                b.Get<int>("globalState"),
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, s) =>
            {
                var input = b.Get<int>("input");
                var globalState = b.Get<int>("globalState");
                var output = b.Get<int>("output");
                int blockSize = (int)s.BlockX;
                int numBlocks = (int)s.GridX;

                int totalSum = 0;
                Span<int> blockSums = stackalloc int[numBlocks];
                for (int bIdx = 0; bIdx < numBlocks; bIdx++)
                {
                    int bSum = 0;
                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = (bIdx * blockSize) + localTid;
                        bSum += (input[gid] & 0x3FF) + 1;
                    }

                    blockSums[bIdx] = bSum;
                    totalSum += bSum;
                }

                globalState[0] = 500;
                globalState[1] = totalSum;
                globalState[2] = 500 + totalSum;

                for (int bIdx = 0; bIdx < numBlocks; bIdx++)
                {
                    int bSum = blockSums[bIdx];
                    for (int localTid = 0; localTid < blockSize; localTid++)
                    {
                        int gid = (bIdx * blockSize) + localTid;
                        output[gid] = globalState[2] + bSum + localTid;
                    }
                }
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CooperativeMultiPhaseReduceNormalize_MatchesTheReferenceAcross1DAnd2DGrids() =>
        DifferentialRunner.Run(s_cooperativeMultiPhaseReduceNormalize);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CooperativePingPongStencilAndXorSwap_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_cooperativePingPongStencilAndXorSwap);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CooperativeSyncGridBodyControlFlowAndDeviceFn_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_cooperativeSyncGridBodyControlFlowAndDeviceFn);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleThreadMultiDimAndReturn_ExecutesExactlyOnceAcrossAllSixAxes() =>
        DifferentialRunner.Run(s_singleThreadMultiDimAndReturn);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SingleThreadCooperativeBarrier_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_singleThreadCooperativeBarrier);

    // -------------------------------------------------------------------------
    // Stage 4: Compiler / Analyzer / Generator verification for #694 (S25) and S26
    // -------------------------------------------------------------------------

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("StandardCpp")]
    [InlineData("Cuda")]
    public async Task Test_Stage4_DirectVsIndirectSyncGridInNonCooperativeKernel_DiagnosticBehavior(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            public static partial class Stage4SyncGridAuditKernels
            {
                [GpuDeviceFunction]
                public static void HelperWithSyncGrid()
                {
                    Gpu.SyncGrid();
                }

                [GpuKernel]
                public static void DirectNonCooperativeKernel(int[] data)
                {
                    Gpu.SyncGrid();
                }

                // #694 (S25): Indirect Gpu.SyncGrid() through a [GpuDeviceFunction] in a non-cooperative kernel
                [GpuKernel]
                public static void IndirectNonCooperativeKernel(int[] data)
                {
                    HelperWithSyncGrid();
                }
            }
            """;

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        var gsh0030 = diagnostics.Where(static d => d.Id == "GSH0030").ToList();

        // Direct call in DirectNonCooperativeKernel reports GSH0030; indirect call in IndirectNonCooperativeKernel
        // currently misses GSH0030 (#694 / S25).
        Assert.Single(gsh0030);
        Assert.Contains("DirectNonCooperativeKernel", gsh0030[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("StandardCpp")]
    [InlineData("Cuda")]
    public async Task Test_Stage4_GpuAtomicVsInterlockedBarrierUniformity_DiagnosticBehavior(string dialect)
    {
        const string source = """
            using System.Threading;
            using Kassyi.GSharp.Core;

            public static partial class Stage4AtomicUniformityAuditKernels
            {
                [GpuKernel]
                public static void GpuAtomicBranchWithBarrierKernel(int[] outMask)
                {
                    int localSlot = 0;
                    if (Gpu.AtomicAdd(ref localSlot, 1) == 0)
                    {
                        outMask[0] = (int)Warp.ActiveMask();
                        Gpu.SyncThreads();
                    }

                    Gpu.SyncThreads();
                }

                // S26: Interlocked.Add return value is not classified as thread-variant in SyncIntrinsicClassifier,
                // so neither GSH0083 (for Gpu.SyncThreads() on a uniform local ref) nor GSH0101 (for Warp.ActiveMask() on CPU)
                // is reported inside the Interlocked.Add branch.
                [GpuKernel]
                public static void InterlockedBranchWithBarrierKernel(int[] outMask)
                {
                    int localSlot = 0;
                    if (Interlocked.Add(ref localSlot, 1) == 1)
                    {
                        outMask[0] = (int)Warp.ActiveMask();
                        Gpu.SyncThreads();
                    }

                    Gpu.SyncThreads();
                }
            }
            """;

        var diagnostics = await KernelDiagnostics.RunAsync(source, dialect);
        var gsh0083 = diagnostics.Where(static d => d.Id == "GSH0083").ToList();

        // Gpu.AtomicAdd branch reports GSH0083 at line 13; Interlocked.Add branch at line 28 currently misses GSH0083 (S26).
        Assert.Single(gsh0083);
        Assert.Equal(13, gsh0083[0].StartLine);

        if (dialect == "StandardCpp")
        {
            var gsh0101 = diagnostics.Where(static d => d.Id == "GSH0101").ToList();
            Assert.Single(gsh0101);
            Assert.Equal(12, gsh0101[0].StartLine);
        }
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("StandardCpp")]
    [InlineData("Cuda")]
    public void Test_Stage4_InterlockedAdd64CastAndSharedMemoryDeclaration_GeneratorOutput(string dialect)
    {
        const string source = """
            using System.Threading;
            using Kassyi.GSharp.Core;

            public static partial class Stage4AtomicAndSharedMemoryEmissionKernels
            {
                [GpuKernel]
                public static void Atomic64AndSharedEmissionKernel(long[] slot, int[] outInt)
                {
                    var sBuf = new SharedMemory<int>(8);
                    int ltZero = Interlocked.Add(ref slot[0], -5L) < 0L ? 1 : 0;
                    int shifted = (int)(Interlocked.Exchange(ref slot[1], 42L) >> 62);
                    if (ThreadIndex.X == 0)
                    {
                        sBuf[0] = ltZero + shifted;
                    }

                    Gpu.SyncThreads();
                    outInt[ThreadIndex.X] = sBuf[0];
                }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, dialect);
        string generated = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).Select(static s => s.SourceText.ToString()));

        // S23: Interlocked.Add(ref long, long) and Interlocked.Exchange(ref long, long) emit unsigned long long* atomics
        // without casting the return expression back to signed long long.
        Assert.Contains("__gsharp_atomicAdd((unsigned long long*)(&", generated, StringComparison.Ordinal);
        Assert.Contains("atomicExch((unsigned long long*)(&", generated, StringComparison.Ordinal);

        if (dialect == "Cuda")
        {
            // Verify static __shared__ declaration in CUDA C++ has no invalid C++ '= {}' initializer (#694).
            Assert.Contains("__shared__ int __gsharp_id_2_sBuf[8];", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__shared__ int __gsharp_id_2_sBuf[8] =", generated, StringComparison.Ordinal);
        }
    }
}
