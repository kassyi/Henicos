// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential test kernels for <see cref="Gpu.SyncGrid()"/>, <see cref="Gpu.SyncGrid(System.Action)"/>,
/// <see cref="Gpu.SingleThread(System.Action)"/>, and cooperative multi-phase barrier + atomic workflows.
/// </summary>
public static partial class GridSyncAndSingleThreadDifferentialKernels
{
    /// <summary>Per-thread output columns for <see cref="CooperativeMultiPhaseReduceNormalizeKernel"/> (F5a).</summary>
    public static readonly string[] MultiPhaseReduceColumns =
    [
        "own_block_sum",
        "next_block_sum",
        "grid_total_from_single_thread",
        "normalized_diff_from_max_block",
    ];

    /// <summary>Grid-wide summary columns for <see cref="CooperativeMultiPhaseReduceNormalizeKernel"/> (F5a).</summary>
    public static readonly string[] MultiPhaseGridStatsColumns =
    [
        "single_thread_grid_total",
        "single_thread_max_block_sum",
        "atomic_grid_total",
    ];

    /// <summary>Per-thread output columns for <see cref="SingleThreadMultiDimAndReturnKernel"/> (F6a).</summary>
    public static readonly string[] SingleThreadMultiDimColumns =
    [
        "linear_gid",
        "block_smem_broadcast",
        "post_single_thread_marker",
    ];

    /// <summary>Counter columns for <see cref="SingleThreadMultiDimAndReturnKernel"/> (F6a).</summary>
    public static readonly string[] SingleThreadCounterColumns =
    [
        "exec_count_block1",
        "device_fn_seed",
        "exec_count_early_return",
        "local_fn_result",
    ];

    [GpuDeviceFunction]
    public static void TryComputeOut(int value, out int result)
    {
        result = (value * 3) - 7;
    }

    [GpuDeviceFunction]
    public static int ComputeLeaderSeed(int baseValue) =>
        (baseValue * 17) + 0x1234;

    [GpuDeviceFunction]
    public static void InnerGridStep(int[] phaseBuf, int[] scratch, int gid, int total)
    {
        int partner = (gid + 3) % total;
        scratch[gid] = phaseBuf[gid] ^ (phaseBuf[partner] + 19);
        Gpu.SyncGrid();
    }

    [GpuDeviceFunction]
    public static void OuterGridStep(int[] phaseBuf, int[] scratch, int[] output, int gid, int total)
    {
        InnerGridStep(phaseBuf, scratch, gid, total);
        int prev = (gid + total - 1) % total;
        output[gid] = scratch[gid] + scratch[prev];
    }

    // -------------------------------------------------------------------------
    // F5a: Cooperative multi-phase reduction and normalization across 1D and 2D
    //      grids using dynamic SharedMemory<int>, Gpu.SyncThreads(), Gpu.SyncGrid(),
    //      Gpu.SingleThread(), and Gpu.AtomicAdd.
    // -------------------------------------------------------------------------
    [GpuKernel(Cooperative = true)]
    public static void CooperativeMultiPhaseReduceNormalizeKernel(
        int[] input,
        int[] blockSums,
        int[] gridStats,
        int[] output,
        int dynBlockCap)
    {
        int localTid = ThreadIndex.X + (ThreadIndex.Y * BlockDimension.X);
        int blockId = BlockIndex.X + (BlockIndex.Y * GridDimension.X);
        int blockSize = BlockDimension.X * BlockDimension.Y;
        int numBlocks = GridDimension.X * GridDimension.Y;
        int gid = (blockId * blockSize) + localTid;

        var sBlock = new SharedMemory<int>(dynBlockCap);
        int v = (input[gid] & 0x7FF) - 0x3FF;
        sBlock[localTid] = v;
        Gpu.SyncThreads();

        if (localTid == 0)
        {
            int bSum = 0;
            for (int i = 0; i < blockSize; i++)
            {
                bSum += sBlock[i];
            }

            blockSums[blockId] = bSum;
            Gpu.AtomicAdd(ref gridStats[2], bSum);
        }

        Gpu.SyncGrid();

        Gpu.SingleThread(() =>
        {
            int total = 0;
            int maxBlock = int.MinValue;
            for (int b = 0; b < numBlocks; b++)
            {
                int bs = blockSums[b];
                total += bs;
                if (bs > maxBlock)
                {
                    maxBlock = bs;
                }
            }

            gridStats[0] = total;
            gridStats[1] = maxBlock;
        });

        Gpu.SyncGrid();

        int ownBlockSum = blockSums[blockId];
        int nextBlockSum = blockSums[(blockId + 1) % numBlocks];
        int gridTotal = gridStats[0];
        int maxBlockSum = gridStats[1];

        int o = gid * 4;
        output[o + 0] = ownBlockSum;
        output[o + 1] = nextBlockSum;
        output[o + 2] = gridTotal;
        output[o + 3] = (v * numBlocks) + (maxBlockSum - ownBlockSum);
    }

    // -------------------------------------------------------------------------
    // F5b: Cooperative multi-step iterative ping-pong cross-block halo stencil
    //      and CLSmith-style gated XOR-pair exchange with two Gpu.SyncGrid()
    //      barriers per loop iteration.
    // -------------------------------------------------------------------------
    [GpuKernel(Cooperative = true)]
    public static void CooperativePingPongStencilAndXorSwapKernel(
        int[] state,
        int[] scratch,
        int steps)
    {
        int gid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        int total = GridDimension.X * BlockDimension.X;

        for (int step = 0; step < steps; step++)
        {
            int prev = (gid + total - 1) % total;
            int next = (gid + 1) % total;
            int oppositeBlockPeer = (gid + (total >> 1)) % total;

            int selfVal = state[gid];
            int haloDelta = (state[prev] - state[next]) ^ (state[oppositeBlockPeer] & 0xFF);
            scratch[gid] = selfVal + haloDelta + (step * 7);

            Gpu.SyncGrid();

            int partner = gid ^ 1;
            int a = scratch[gid];
            int b = scratch[partner];
            int mixed = (gid & 1) == 0
                ? a + ((b & 0x3F) - 0x1F)
                : b - ((a & 0x3F) - 0x1F);
            state[gid] = mixed;

            Gpu.SyncGrid();
        }
    }

    // -------------------------------------------------------------------------
    // F5c: Gpu.SyncGrid(Action) with divergent early return, local & out variable
    //      declarations after return, switch with return, loop-nested SyncGrid(body),
    //      and 2-level nested [GpuDeviceFunction] calling Gpu.SyncGrid().
    // -------------------------------------------------------------------------
    [GpuKernel(Cooperative = true)]
    public static void CooperativeSyncGridBodyControlFlowAndDeviceFnKernel(
        int[] input,
        int[] phaseBuf,
        int[] scratch,
        int[] output)
    {
        int gid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
        int total = GridDimension.X * BlockDimension.X;

        // Phase 1: Gpu.SyncGrid(body) with early return on even threads and local + out var declarations after return
        Gpu.SyncGrid(() =>
        {
            if ((gid & 1) == 0)
            {
                phaseBuf[gid] = (input[gid] & 0xFFF) + 100;
                return;
            }

            int oddBase = (input[gid] & 0xFFF) + 200;
            TryComputeOut(oddBase, out int oddTransformed);
            phaseBuf[gid] = oddTransformed;
        });

        Gpu.SyncGrid();

        // Phase 2: Gpu.SyncGrid(body) reading cross-thread neighbor from Phase 1 and branching via switch with return
        Gpu.SyncGrid(() =>
        {
            int nextNeighbor = phaseBuf[(gid + 1) % total];
            switch (gid & 3)
            {
                case 0:
                    scratch[gid] = nextNeighbor + 10;
                    return;
                case 1:
                    scratch[gid] = nextNeighbor + 20;
                    break;
                case 2:
                    scratch[gid] = nextNeighbor + 30;
                    return;
                default:
                    scratch[gid] = nextNeighbor + 40;
                    break;
            }

            scratch[gid] += 5;
        });

        Gpu.SyncGrid();

        // Phase 3: Loop containing Gpu.SyncGrid(body) reading cross-block opposite peer
        for (int round = 0; round < 2; round++)
        {
            Gpu.SyncGrid(() =>
            {
                int opposite = scratch[(gid + (total >> 1)) % total];
                phaseBuf[gid] = phaseBuf[gid] + opposite + (round * 11);
            });

            Gpu.SyncGrid();
        }

        // Phase 4: 2-level nested [GpuDeviceFunction] (OuterGridStep -> InnerGridStep) calling parameterless Gpu.SyncGrid()
        OuterGridStep(phaseBuf, scratch, output, gid, total);
    }

    // -------------------------------------------------------------------------
    // F6a: Gpu.SingleThread(Action) across 3D grid x 3D block (6-axis) launch
    //      shapes (#690), early return inside SingleThread (#693), local function
    //      return inside SingleThread, and non-suspending [GpuDeviceFunction]
    //      invocation inside SingleThread (#667).
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SingleThreadMultiDimAndReturnKernel(
        int[] counters,
        int[] output,
        int seedArg)
    {
        int localTid = ThreadIndex.X
            + (ThreadIndex.Y * BlockDimension.X)
            + (ThreadIndex.Z * BlockDimension.X * BlockDimension.Y);
        int blockSize = BlockDimension.X * BlockDimension.Y * BlockDimension.Z;
        int blockId = BlockIndex.X
            + (BlockIndex.Y * GridDimension.X)
            + (BlockIndex.Z * GridDimension.X * GridDimension.Y);
        int gid = (blockId * blockSize) + localTid;

        var sBroadcast = new SharedMemory<int>(1);
        if (localTid == 0)
        {
            sBroadcast[0] = (blockId + 1) * 100;
        }

        Gpu.SyncThreads();

        // 1. SingleThread with [GpuDeviceFunction] call (#667) and 6-axis single execution check (#690)
        Gpu.SingleThread(() =>
        {
            Gpu.AtomicAdd(ref counters[0], 1);
            counters[1] = ComputeLeaderSeed(seedArg);
        });

        // 2. SingleThread with early return (#693) and local function return
        Gpu.SingleThread(() =>
        {
            int LocalCombine(int x, int y)
            {
                return (x * 10) + y;
            }

            Gpu.AtomicAdd(ref counters[2], 1);
            counters[3] = LocalCombine(seedArg, 7);
            if (seedArg >= 0)
            {
                return;
            }

            counters[3] = -999;
        });

        Gpu.SyncThreads();

        int o = gid * 3;
        output[o + 0] = gid;
        output[o + 1] = sBroadcast[0];
        output[o + 2] = seedArg + localTid + 1;
    }

    // -------------------------------------------------------------------------
    // F6b: Cooperative kernel combining Gpu.SingleThread global initialization ->
    //      Gpu.SyncGrid() -> per-block SharedMemory + AtomicAdd -> Gpu.SyncGrid()
    //      -> Gpu.SingleThread global finalization -> Gpu.SyncGrid().
    // -------------------------------------------------------------------------
    [GpuKernel(Cooperative = true)]
    public static void SingleThreadCooperativeBarrierKernel(
        int[] input,
        int[] globalState,
        int[] output)
    {
        int localTid = ThreadIndex.X;
        int blockId = BlockIndex.X;
        int blockSize = BlockDimension.X;
        int gid = (blockId * blockSize) + localTid;

        var sBlockSum = new SharedMemory<int>(1);
        if (localTid == 0)
        {
            sBlockSum[0] = 0;
        }

        Gpu.SingleThread(() =>
        {
            globalState[0] = 500;
            globalState[1] = 0;
            globalState[2] = 0;
        });

        Gpu.SyncGrid();
        Gpu.SyncThreads();

        int v = (input[gid] & 0x3FF) + 1;
        Gpu.AtomicAdd(ref sBlockSum[0], v);

        Gpu.SyncThreads();

        if (localTid == 0)
        {
            Gpu.AtomicAdd(ref globalState[1], sBlockSum[0]);
        }

        Gpu.SyncGrid();

        Gpu.SingleThread(() =>
        {
            globalState[2] = globalState[0] + globalState[1];
        });

        Gpu.SyncGrid();

        output[gid] = globalState[2] + sBlockSum[0] + localTid;
    }
}
