// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for Launch Shape &amp; 3D Index Mapping (Lane D1: <c>D1-1</c> through <c>D1-6</c>).
/// </summary>
public static partial class LaunchShapeDifferentialKernels
{
    /// <summary>The columns of <c>outSignedMath</c> in <see cref="ThreeDimensionalIndexAndSignednessKernel"/>.</summary>
    public static readonly string[] SignedMathColumns =
    [
        "tidMinusBdimSum",
        "bidMinusGdimWeighted",
        "unaryNegBitwiseXor",
        "signedDivModShift",
    ];

    /// <summary>The columns of <c>header</c> in <see cref="SingleThreadMultiDimAndEarlyReturnKernel"/>.</summary>
    public static readonly string[] SingleThreadHeaderColumns =
    [
        "leaderExecutionCount",
        "leaderCoordinateTag",
        "leaderBranchMarker",
    ];

    /// <summary>The <see cref="int"/> shuffle columns of <see cref="CrossAxisWarpShuffleAndActiveMaskKernel"/>.</summary>
    public static readonly string[] CrossAxisShuffleIntColumns =
    [
        "ShuffleSync(v,0)",
        "ShuffleUpSync(v,1)",
        "ShuffleXorSync(v,safeXor1)",
        "ShuffleSync(v,safeRing4,w4)",
    ];

    // -------------------------------------------------------------------------
    // D1-1: 3D Index Linearization, Bijection & Signed Arithmetic
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void ThreeDimensionalIndexAndSignednessKernel(
        long[] outGid,
        int[] outSignedMath,
        int[] visitCount,
        int rows)
    {
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;
        int tz = ThreadIndex.Z;

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int bz = BlockIndex.Z;

        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;

        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;
        int gdz = GridDimension.Z;

        int blockLinearTid = tx + (ty * bdx) + (tz * bdx * bdy);
        int threadsPerBlock = bdx * bdy * bdz;
        long gridLinearBid = bx + ((long)by * gdx) + ((long)bz * gdx * gdy);
        long gid = (gridLinearBid * threadsPerBlock) + blockLinearTid;

        if (gid < rows)
        {
            int idx = (int)gid;
            outGid[idx] = gid;

            int baseOffset = idx * 4;
            // 1. Negative difference between thread index and block dimension (always negative)
            outSignedMath[baseOffset + 0] = (tx - bdx) + (ty - bdy) + (tz - bdz);
            // 2. Negative difference between block index and grid dimension with prime weights
            outSignedMath[baseOffset + 1] = ((bx - gdx) * 3) + ((by - gdy) * 5) + ((bz - gdz) * 7);
            // 3. Unary negation and bitwise complement interaction
            outSignedMath[baseOffset + 2] = (-tx - 1) ^ (-bx - 1) ^ (-bdz);
            // 4. Signed division, remainder, and arithmetic right shift on negative index differences
            outSignedMath[baseOffset + 3] = ((tx - ty) / 2) + ((bx - by) % 3) + ((tz - bz) >> 1);

            Gpu.AtomicAdd(ref visitCount[idx], 1);
        }
    }

    // -------------------------------------------------------------------------
    // D1-2: Gpu.GridStrideLoop 3D & Metamorphic Shape Invariance
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void GridStrideLoopMetamorphicKernel(
        int[] src,
        int[] dstMulti,
        int[] dstShort,
        int[] zeroGuard,
        int multiN,
        int shortN,
        int bias)
    {
        // 1. Multi-iteration pass (multiN > totalThreads) using stateless overload
        Gpu.GridStrideLoop(multiN, i =>
        {
            Gpu.AtomicAdd(ref dstMulti[i], (src[i] ^ (i * 0x45D9F3B)) + bias);
        });

        // 2. Short pass (shortN < totalThreads) using stateful TContext overload
        int state = bias ^ 0x13579BDF;
        Gpu.GridStrideLoop(ref state, shortN, (ref int s, int i) =>
        {
            Gpu.AtomicAdd(ref dstShort[i], src[i] + s + (i * 17));
        });

        // 3. Zero and negative maxBounds must execute zero times
        Gpu.GridStrideLoop(0, i =>
        {
            Gpu.AtomicAdd(ref zeroGuard[0], 1);
        });

        Gpu.GridStrideLoop(-5, i =>
        {
            Gpu.AtomicAdd(ref zeroGuard[1], 1);
        });
    }

    // -------------------------------------------------------------------------
    // D1-3: Gpu.SingleThread 3D Origin & Early-Return Semantics (#693)
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void SingleThreadMultiDimAndEarlyReturnKernel(
        int[] header,
        int[] perThreadOut,
        int rows,
        int earlyExitLeader)
    {
        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;
        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;

        int blockTid = ThreadIndex.X + (ThreadIndex.Y * bdx) + (ThreadIndex.Z * bdx * bdy);
        long blockBid = BlockIndex.X + ((long)BlockIndex.Y * gdx) + ((long)BlockIndex.Z * gdx * gdy);
        long gid = (blockBid * (bdx * bdy * bdz)) + blockTid;

        Gpu.SingleThread(() =>
        {
            Gpu.AtomicAdd(ref header[0], 1);
            header[1] = (ThreadIndex.X * 100) + (ThreadIndex.Y * 10) + ThreadIndex.Z
                + (BlockIndex.X * 10000) + (BlockIndex.Y * 1000) + (BlockIndex.Z * 100000);

            if (earlyExitLeader != 0)
            {
                header[2] = 111;
                return;
            }

            header[2] = 222;
        });

        // Per C# and G# specification (#693), a `return` inside `Gpu.SingleThread` leaves only the
        // SingleThread lambda body; statements after `Gpu.SingleThread` still run on thread (0,0,0).
        if (gid < rows)
        {
            int idx = (int)gid;
            perThreadOut[idx] = (idx * 3) + 7;
        }
    }

    // -------------------------------------------------------------------------
    // D1-4: Cross-Axis 2D/3D Warp Shuffle & Partial-Warp ActiveMask
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void CrossAxisWarpShuffleAndActiveMaskKernel(
        int[] srcInt,
        float[] srcFloat,
        uint[] srcUInt,
        uint[] outMask,
        int[] outShflInt,
        float[] outShflFloat,
        uint[] outShflUInt)
    {
        uint mask = Warp.ActiveMask();

        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;
        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;

        int blockTid = ThreadIndex.X + (ThreadIndex.Y * bdx) + (ThreadIndex.Z * bdx * bdy);
        int blockThreads = bdx * bdy * bdz;
        long blockBid = BlockIndex.X + ((long)BlockIndex.Y * gdx) + ((long)BlockIndex.Z * gdx * gdy);
        int gid = (int)((blockBid * blockThreads) + blockTid);

        int rows = srcInt.Length;
        int v = gid < rows ? srcInt[gid] : (blockTid + 1);
        float fv = gid < rows ? srcFloat[gid] : (blockTid * 0.5f);
        uint uv = gid < rows ? srcUInt[gid] : (uint)(blockTid * 3 + 7);

        int lane = blockTid & 31;
        int warpStart = blockTid & ~31;
        int rem = blockThreads - warpStart;
        int activeLanes = rem >= 32 ? 32 : (rem > 0 ? rem : 0);

        int shfl0 = Warp.ShuffleSync(mask, v, 0);
        int up1 = Warp.ShuffleUpSync(mask, v, 1u);

        int safeXor1 = ((lane ^ 1) < activeLanes) ? 1 : 0;
        int xor1 = Warp.ShuffleXorSync(mask, v, safeXor1);

        int seg4Target = (lane & ~3) + ((lane + 1) & 3);
        int safeSeg4Lane = seg4Target < activeLanes ? (seg4Target & 3) : (lane & 3);
        int shflSeg4 = Warp.ShuffleSync(mask, v, safeSeg4Lane, 4);

        uint safeDown1 = (lane + 1 < activeLanes) ? 1u : 0u;
        float fDown1 = Warp.ShuffleDownSync(mask, fv, safeDown1);
        uint uXor1 = Warp.ShuffleXorSync(mask, uv, safeXor1);

        if (gid < rows)
        {
            outMask[gid] = mask;
            int baseIdx = gid * 4;
            outShflInt[baseIdx + 0] = shfl0;
            outShflInt[baseIdx + 1] = up1;
            outShflInt[baseIdx + 2] = xor1;
            outShflInt[baseIdx + 3] = shflSeg4;
            outShflFloat[gid] = fDown1;
            outShflUInt[gid] = uXor1;
        }
    }

    // -------------------------------------------------------------------------
    // D1-5: 2D/3D SharedMemory & Gpu.SyncThreads State-Machine Lowering
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void MultiDimSharedMemoryAndBarrierKernel(
        int[] src,
        int[] outPermuted,
        int[] outBlockSum)
    {
        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;
        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;

        int blockTid = ThreadIndex.X + (ThreadIndex.Y * bdx) + (ThreadIndex.Z * bdx * bdy);
        int blockThreads = bdx * bdy * bdz;
        long blockBid = BlockIndex.X + ((long)BlockIndex.Y * gdx) + ((long)BlockIndex.Z * gdx * gdy);
        int gid = (int)((blockBid * blockThreads) + blockTid);
        int rows = src.Length;

        var staticBuf = new SharedMemory<int>(128);
        var dynamicBuf = new SharedMemory<int>(blockThreads);

        int val = gid < rows ? src[gid] : 0;
        staticBuf[blockTid] = val;
        dynamicBuf[blockTid] = val * 2;

        Gpu.SyncThreads();

        int partnerTid = blockThreads - 1 - blockTid;
        int permuted = staticBuf[partnerTid] + dynamicBuf[(blockTid + 1) % blockThreads];

        Gpu.SyncThreads();

        staticBuf[blockTid] = permuted;
        Gpu.SyncThreads();

        if (blockTid == 0)
        {
            int sum = 0;
            for (int i = 0; i < blockThreads; i++)
            {
                sum += staticBuf[i];
            }

            dynamicBuf[0] = sum;
        }

        Gpu.SyncThreads();

        if (gid < rows)
        {
            outPermuted[gid] = permuted;
            outBlockSum[gid] = dynamicBuf[0];
        }
    }

    // -------------------------------------------------------------------------
    // D1-6: Value-Returning Kernel (AutoResult) across 2D/3D Shapes
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static int AutoResultThreeDimensionalKernel(int[] src, int salt)
    {
        int bdx = BlockDimension.X;
        int bdy = BlockDimension.Y;
        int bdz = BlockDimension.Z;
        int gdx = GridDimension.X;
        int gdy = GridDimension.Y;

        int blockTid = ThreadIndex.X + (ThreadIndex.Y * bdx) + (ThreadIndex.Z * bdx * bdy);
        int blockThreads = bdx * bdy * bdz;
        long blockBid = BlockIndex.X + ((long)BlockIndex.Y * gdx) + ((long)BlockIndex.Z * gdx * gdy);
        int gid = (int)((blockBid * blockThreads) + blockTid);

        int coordMix = (ThreadIndex.X * 3)
            + (ThreadIndex.Y * 5)
            + (ThreadIndex.Z * 7)
            + (BlockIndex.X * 11)
            + (BlockIndex.Y * 13)
            + (BlockIndex.Z * 17);

        int v = gid < src.Length ? src[gid] : coordMix;
        return (v ^ salt) + coordMix;
    }
}
