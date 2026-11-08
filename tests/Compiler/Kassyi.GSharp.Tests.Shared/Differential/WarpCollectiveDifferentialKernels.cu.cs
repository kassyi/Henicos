using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels for the warp identity (<see cref="Gpu.LaneId"/>, <see cref="Gpu.WarpId"/>), the warp votes and reductions,
/// and warp shuffles in a <see cref="Gpu.GridStrideLoop(int, System.Action{int})"/> body, run on every path (#845, #1091).
/// </summary>
public static partial class WarpCollectiveDifferentialKernels
{
    /// <summary>The columns of <see cref="WarpVoteAndReduceKernel"/>.</summary>
    public static readonly string[] VoteColumns =
    [
        "ballot.full",
        "all.full",
        "any.full",
        "ballot.half",
        "all.half",
        "any.half",
        "reduceAdd.int",
        "reduceMin.int",
        "reduceMax.int",
        "reduceAnd.int",
        "reduceOr.int",
        "reduceXor.int",
        "reduceAdd.uint",
        "reduceMin.uint",
        "reduceMax.uint",
        "reduceAdd.int.half",
        "reduceMin.uint.half",
    ];

    /// <summary>The columns of <see cref="GridStrideShuffleInDeviceFunctionKernel"/> and <see cref="GridStrideShuffleWithContextKernel"/>.</summary>
    public static readonly string[] GridStrideColumns = ["iter0", "iter1"];

    // The rank of the thread in the launch: blocks in order, x fastest inside a block
    [GpuDeviceFunction]
    private static int LinearThreadInBlock() =>
        (ThreadIndex.Z * BlockDimension.Y + ThreadIndex.Y) * BlockDimension.X + ThreadIndex.X;

    [GpuDeviceFunction]
    private static int GlobalThread()
    {
        int block = (BlockIndex.Z * GridDimension.Y + BlockIndex.Y) * GridDimension.X + BlockIndex.X;
        return block * BlockDimension.X * BlockDimension.Y * BlockDimension.Z + LinearThreadInBlock();
    }

    /// <summary>
    /// Writes <see cref="Gpu.LaneId"/> and <see cref="Gpu.WarpId"/> of every thread, once before and once after a block
    /// barrier (so the CPU paths read them in a state machine as well).
    /// </summary>
    [GpuKernel]
    public static void WarpIdentityKernel(int[] laneOut, int[] warpOut)
    {
        int gid = GlobalThread();
        laneOut[gid * 2] = Gpu.LaneId();
        warpOut[gid * 2] = Gpu.WarpId();
        Gpu.SyncThreads();
        laneOut[gid * 2 + 1] = Gpu.LaneId();
        warpOut[gid * 2 + 1] = Gpu.WarpId();
    }

    /// <summary>
    /// Every vote and reduction with the full mask and with the mask of the caller's half of the warp, on values from
    /// <paramref name="input"/>. A block whose size is not a multiple of 32 leaves lanes of its last warp out.
    /// </summary>
    [GpuKernel]
    public static void WarpVoteAndReduceKernel(int[] input, uint[] output)
    {
        int gid = GlobalThread();
        int local = LinearThreadInBlock();
        int lane = local & 31;
        int v = input[gid];
        int o = gid * 17;
        const uint FullMask = 0xFFFFFFFFu;

        // The predicate of All holds in every lane of the even warps
        bool allPredicate = (v & 1) == 0 || ((local >> 5) & 1) == 0;
        // The predicate of Any holds in no lane of the odd warps
        bool anyPredicate = (v & 15) == 0 && ((local >> 5) & 1) == 0;

        output[o + 0] = Warp.BallotSync(FullMask, (v & 1) != 0);
        output[o + 1] = Warp.AllSync(FullMask, allPredicate) ? 1u : 0u;
        output[o + 2] = Warp.AnySync(FullMask, anyPredicate) ? 1u : 0u;

        uint halfMask = lane < 16 ? 0x0000FFFFu : 0xFFFF0000u;
        if (lane < 16)
        {
            output[o + 3] = Warp.BallotSync(0x0000FFFFu, (v & 2) != 0);
            output[o + 4] = Warp.AllSync(0x0000FFFFu, allPredicate) ? 1u : 0u;
            output[o + 5] = Warp.AnySync(0x0000FFFFu, anyPredicate) ? 1u : 0u;
        }
        else
        {
            output[o + 3] = Warp.BallotSync(0xFFFF0000u, (v & 2) != 0);
            output[o + 4] = Warp.AllSync(0xFFFF0000u, allPredicate) ? 1u : 0u;
            output[o + 5] = Warp.AnySync(0xFFFF0000u, anyPredicate) ? 1u : 0u;
        }

        output[o + 6] = (uint)Warp.ReduceAddSync(FullMask, v);
        output[o + 7] = (uint)Warp.ReduceMinSync(FullMask, v);
        output[o + 8] = (uint)Warp.ReduceMaxSync(FullMask, v);
        output[o + 9] = (uint)Warp.ReduceAndSync(FullMask, v | 0x10);
        output[o + 10] = (uint)Warp.ReduceOrSync(FullMask, v & 0x0F0F0F0F);
        output[o + 11] = (uint)Warp.ReduceXorSync(FullMask, v);

        uint u = (uint)v;
        output[o + 12] = Warp.ReduceAddSync(FullMask, u);
        output[o + 13] = Warp.ReduceMinSync(FullMask, u);
        output[o + 14] = Warp.ReduceMaxSync(FullMask, u);

        output[o + 15] = (uint)Warp.ReduceAddSync(halfMask, v);
        output[o + 16] = Warp.ReduceMinSync(halfMask, u);
    }

    // A reduction in a device function, which the CPU paths then suspend in; named arguments in another order
    [GpuDeviceFunction]
    private static int CountOddLanes(uint mask, int v) => Warp.ReduceAddSync(val: v & 1, mask: mask);

    /// <summary>
    /// A reduction in a device function, and a ballot with named arguments in another order than the parameters.
    /// </summary>
    [GpuKernel]
    public static void VoteInDeviceFunctionKernel(int[] input, uint[] output)
    {
        int gid = GlobalThread();
        int v = input[gid];
        output[gid * 2] = (uint)CountOddLanes(0xFFFFFFFFu, v);
        output[gid * 2 + 1] = Warp.BallotSync(predicate: (v & 4) != 0, mask: 0xFFFFFFFFu);
    }

    /// <summary>Two rounds of a grid-stride loop, each with a ballot over the values of the round.</summary>
    [GpuKernel]
    public static void GridStrideBallotKernel(int[] input, uint[] output)
    {
        int totalThreads = GridDimension.X * BlockDimension.X;
        Gpu.GridStrideLoop(totalThreads * 2, i =>
        {
            output[(i % totalThreads) * 2 + i / totalThreads] = Warp.BallotSync(0xFFFFFFFFu, (input[i] & 1) != 0);
        });
    }

    /// <summary>A ballot whose mask names the next lane only, not the caller: undefined on CUDA.</summary>
    [GpuKernel]
    public static void BallotCallerOutsideMaskKernel(uint[] output)
    {
        int gid = GlobalThread();
        int lane = LinearThreadInBlock() & 31;
        output[gid] = Warp.BallotSync(1u << ((lane + 1) & 31), true);
    }

    /// <summary>A reduction whose mask leaves the caller out: undefined on CUDA.</summary>
    [GpuKernel]
    public static void ReduceCallerOutsideMaskKernel(int[] output)
    {
        int gid = GlobalThread();
        int lane = LinearThreadInBlock() & 31;
        output[gid] = Warp.ReduceMinSync(~(1u << lane), gid);
    }

    // Shuffles inside a grid-stride loop of a device function, which the CPU paths then suspend in
    [GpuDeviceFunction]
    private static void ShuffleRounds(int[] input, int[] output, int totalThreads)
    {
        Gpu.GridStrideLoop(totalThreads * 2, i =>
        {
            int shuffled = Warp.ShuffleDownSync(0xFFFFFFFFu, input[i], 1);
            output[(i % totalThreads) * 2 + i / totalThreads] = shuffled;
        });
    }

    /// <summary>Two rounds of a grid-stride loop in a device function, each with a shuffle down by one lane.</summary>
    [GpuKernel]
    public static void GridStrideShuffleInDeviceFunctionKernel(int[] input, int[] output)
    {
        ShuffleRounds(input, output, GridDimension.X * BlockDimension.X);
    }

    /// <summary>
    /// The <c>ref ctx</c> overload of the grid-stride loop with a shuffle up by one lane; the context counts the rounds.
    /// </summary>
    [GpuKernel]
    public static void GridStrideShuffleWithContextKernel(int[] input, int[] output)
    {
        int totalThreads = GridDimension.X * BlockDimension.X;
        int round = 0;
        Gpu.GridStrideLoop(ref round, totalThreads * 2, (ref int r, int i) =>
        {
            int shuffled = Warp.ShuffleUpSync(0xFFFFFFFFu, input[i], 1u);
            output[(i % totalThreads) * 2 + r] = shuffled;
            r++;
        });
    }
}
