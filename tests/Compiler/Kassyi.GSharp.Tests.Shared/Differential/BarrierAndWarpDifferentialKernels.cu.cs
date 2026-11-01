// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Generic struct whose constructor accesses a static <see cref="SharedMemory{T}"/> buffer per <typeparamref name="TTag"/>
/// instantiation. On CUDA each monomorphized constructor function owns a distinct <c>__shared__</c> array.
/// </summary>
public struct SharedSlot<TTag> where TTag : unmanaged
{
    public int Value;

    public SharedSlot(bool write, int lane, int inputValue)
    {
        var smem = new SharedMemory<int>(32);
        if (write)
        {
            smem[lane] = inputValue;
            Value = 0;
        }
        else
        {
            Value = smem[lane];
        }
    }
}

/// <summary>
/// Differential kernels (<see cref="SharedMemory{T}"/>, <see cref="Gpu.SyncThreads"/>, <see cref="Warp"/> shuffle
/// intrinsics, and value parameters written before a barrier).
/// </summary>
public static partial class BarrierAndWarpDifferentialKernels
{
    /// <summary>The columns of <see cref="SharedMemoryBlockReductionAndScanKernel"/>.</summary>
    public static readonly string[] SharedMemoryColumns =
    [
        "blockSum",
        "inclusiveScan",
        "exclusiveScan",
        "haloCombined",
        "wideFolded",
    ];

    /// <summary>The columns of <see cref="WarpShuffleFullWidthKernel"/>.</summary>
    public static readonly string[] FullWarpShuffleColumns =
    [
        "ShuffleSync(v,0)",
        "ShuffleSync(v,19)",
        "ShuffleSync(v,37)",
        "ShuffleDownSync(v,1)",
        "ShuffleDownSync(v,7)",
        "ShuffleUpSync(v,1)",
        "ShuffleUpSync(v,9)",
        "ShuffleXorSync(v,1)",
        "ShuffleXorSync(v,13)",
        "butterflySum32",
        "inclusiveScan32",
    ];

    /// <summary>The columns of <see cref="WarpShuffleSubWarpWidthsKernel"/>.</summary>
    public static readonly string[] SubWarpShuffleColumns =
    [
        "ShuffleSync(v,3,w8)",
        "ShuffleSync(v,13,w8)",
        "ShuffleDownSync(v,3,w8)",
        "ShuffleUpSync(v,2,w8)",
        "ShuffleXorSync(v,3,w8)",
        "ShuffleXorSync(v,8,w8)",
        "ShuffleDownSync(v,5,w16)",
        "ShuffleUpSync(v,5,w16)",
        "ShuffleSync(v,2,w4)",
        "ShuffleDownSync(v,0,w1)",
    ];

    /// <summary>The <see cref="int"/> columns of <see cref="HybridWarpAndSharedMemoryBlockReduceKernel"/>.</summary>
    public static readonly string[] HybridReduceColumns =
    [
        "blockTotal",
        "otherWarpSum",
    ];

    /// <summary>The <see cref="float"/> columns of <see cref="HybridWarpAndSharedMemoryBlockReduceKernel"/>.</summary>
    public static readonly string[] HybridFloatShuffleColumns =
    [
        "ShuffleDownSync(fv,3)",
        "ShuffleUpSync(fv,2)",
    ];

    /// <summary>The columns of <see cref="SubWarpConditionalShuffleKernel"/>.</summary>
    public static readonly string[] SubWarpConditionalShuffleColumns =
    [
        "halfWarpDown1",
        "reconvergedFullWarpXor16",
    ];

    /// <summary>The columns of <see cref="GenericStructStaticSharedMemoryKernel"/>.</summary>
    public static readonly string[] GenericStructSharedColumns =
    [
        "SharedSlot<int>",
        "SharedSlot<uint>",
    ];

    // 1. Shared memory block-wide sum reduction, Hillis-Steele inclusive/exclusive prefix scans,
    //    block reversal + circular halo difference, and multi-type shared memory (blockDimX = 64).
    [GpuKernel]
    public static void SharedMemoryBlockReductionAndScanKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + tid;
        int n = input.Length;

        var sReduce = new SharedMemory<int>(64);
        var sScanA = new SharedMemory<int>(64);
        var sScanB = new SharedMemory<int>(64);
        var sHalo = new SharedMemory<int>(64);
        var sWide = new SharedMemory<long>(64);

        int v = gid < n ? (input[gid] & 0x3FFF) - 0x1FFF : 0;
        sReduce[tid] = v;
        sScanA[tid] = v;
        sHalo[tid] = v;
        sWide[tid] = (long)v * 3L + tid;
        Gpu.SyncThreads();

        // Tree reduction over 64 threads
        if (tid < 32)
        {
            sReduce[tid] = sReduce[tid] + sReduce[tid + 32];
        }

        Gpu.SyncThreads();
        if (tid < 16)
        {
            sReduce[tid] = sReduce[tid] + sReduce[tid + 16];
        }

        Gpu.SyncThreads();
        if (tid < 8)
        {
            sReduce[tid] = sReduce[tid] + sReduce[tid + 8];
        }

        Gpu.SyncThreads();
        if (tid < 4)
        {
            sReduce[tid] = sReduce[tid] + sReduce[tid + 4];
        }

        Gpu.SyncThreads();
        if (tid < 2)
        {
            sReduce[tid] = sReduce[tid] + sReduce[tid + 2];
        }

        Gpu.SyncThreads();
        if (tid == 0)
        {
            sReduce[0] = sReduce[0] + sReduce[1];
        }

        // Hillis-Steele inclusive prefix scan over 64 threads (6 ping-pong steps)
        sScanB[tid] = tid >= 1 ? sScanA[tid] + sScanA[tid - 1] : sScanA[tid];
        Gpu.SyncThreads();

        sScanA[tid] = tid >= 2 ? sScanB[tid] + sScanB[tid - 2] : sScanB[tid];
        Gpu.SyncThreads();

        sScanB[tid] = tid >= 4 ? sScanA[tid] + sScanA[tid - 4] : sScanA[tid];
        Gpu.SyncThreads();

        sScanA[tid] = tid >= 8 ? sScanB[tid] + sScanB[tid - 8] : sScanB[tid];
        Gpu.SyncThreads();

        sScanB[tid] = tid >= 16 ? sScanA[tid] + sScanA[tid - 16] : sScanA[tid];
        Gpu.SyncThreads();

        sScanA[tid] = tid >= 32 ? sScanB[tid] + sScanB[tid - 32] : sScanB[tid];
        Gpu.SyncThreads();

        int blockSum = sReduce[0];
        int inclusiveScan = sScanA[tid];
        int exclusiveScan = tid == 0 ? 0 : sScanA[tid - 1];
        int reversed = sHalo[63 - tid];
        int nextNeighbor = sHalo[(tid + 1) & 63];
        int prevNeighbor = sHalo[(tid + 63) & 63];
        int haloCombined = reversed + (nextNeighbor - prevNeighbor);
        long widePair = sWide[tid] + sWide[tid ^ 1];
        int wideFolded = (int)widePair ^ (int)(widePair >> 32);

        if (gid < n)
        {
            int o = gid * 5;
            output[o + 0] = blockSum;
            output[o + 1] = inclusiveScan;
            output[o + 2] = exclusiveScan;
            output[o + 3] = haloCombined;
            output[o + 4] = wideFolded;
        }
    }

    // 2. Full-warp (width = 32) ShuffleSync, ShuffleDownSync, ShuffleUpSync, ShuffleXorSync,
    //    butterfly reduction, and Hillis-Steele warp scan.
    [GpuKernel]
    public static void WarpShuffleFullWidthKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;
        uint mask = 0xFFFFFFFFu;

        int v = tid < n ? (input[tid] & 0xFFFF) - 0x7FFF : 0;

        int bcast0 = Warp.ShuffleSync(mask, v, 0);
        int bcast19 = Warp.ShuffleSync(mask, v, 19);
        int bcastWrap37 = Warp.ShuffleSync(mask, v, 37);

        int down1 = Warp.ShuffleDownSync(mask, v, 1);
        int down7 = Warp.ShuffleDownSync(mask, v, 7);

        int up1 = Warp.ShuffleUpSync(mask, v, 1);
        int up9 = Warp.ShuffleUpSync(mask, v, 9);

        int xor1 = Warp.ShuffleXorSync(mask, v, 1);
        int xor13 = Warp.ShuffleXorSync(mask, v, 13);

        // 5-step warp butterfly sum reduction
        int red = v;
        int peer16 = Warp.ShuffleXorSync(mask, red, 16);
        red += peer16;
        int peer8 = Warp.ShuffleXorSync(mask, red, 8);
        red += peer8;
        int peer4 = Warp.ShuffleXorSync(mask, red, 4);
        red += peer4;
        int peer2 = Warp.ShuffleXorSync(mask, red, 2);
        red += peer2;
        int peer1 = Warp.ShuffleXorSync(mask, red, 1);
        red += peer1;

        // 5-step warp inclusive scan via ShuffleUpSync
        int scan = v;
        int upScan1 = Warp.ShuffleUpSync(mask, scan, 1);
        if (lane >= 1)
        {
            scan += upScan1;
        }

        int upScan2 = Warp.ShuffleUpSync(mask, scan, 2);
        if (lane >= 2)
        {
            scan += upScan2;
        }

        int upScan4 = Warp.ShuffleUpSync(mask, scan, 4);
        if (lane >= 4)
        {
            scan += upScan4;
        }

        int upScan8 = Warp.ShuffleUpSync(mask, scan, 8);
        if (lane >= 8)
        {
            scan += upScan8;
        }

        int upScan16 = Warp.ShuffleUpSync(mask, scan, 16);
        if (lane >= 16)
        {
            scan += upScan16;
        }

        if (tid < n)
        {
            int o = tid * 11;
            output[o + 0] = bcast0;
            output[o + 1] = bcast19;
            output[o + 2] = bcastWrap37;
            output[o + 3] = down1;
            output[o + 4] = down7;
            output[o + 5] = up1;
            output[o + 6] = up9;
            output[o + 7] = xor1;
            output[o + 8] = xor13;
            output[o + 9] = red;
            output[o + 10] = scan;
        }
    }

    // 3. Sub-warp widths (width = 16, 8, 4, 1) and boundary/wrapping semantics.
    [GpuKernel]
    public static void WarpShuffleSubWarpWidthsKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int n = input.Length;
        uint mask = 0xFFFFFFFFu;

        int v = tid < n ? input[tid] : 0;

        int idx3W8 = Warp.ShuffleSync(mask, v, 3, 8);
        int idxWrap13W8 = Warp.ShuffleSync(mask, v, 13, 8);
        int down3W8 = Warp.ShuffleDownSync(mask, v, 3, 8);
        int up2W8 = Warp.ShuffleUpSync(mask, v, 2, 8);
        int xor3W8 = Warp.ShuffleXorSync(mask, v, 3, 8);
        int xorCross8W8 = Warp.ShuffleXorSync(mask, v, 8, 8);
        int down5W16 = Warp.ShuffleDownSync(mask, v, 5, 16);
        int up5W16 = Warp.ShuffleUpSync(mask, v, 5, 16);
        int idx2W4 = Warp.ShuffleSync(mask, v, 2, 4);
        int down0W1 = Warp.ShuffleDownSync(mask, v, 0, 1);

        if (tid < n)
        {
            int o = tid * 10;
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

    // 4. Hybrid two-warp block reduction (Warp.ShuffleDownSync + SharedMemory<int> + Gpu.SyncThreads())
    //    and float warp shuffle bitwise round-trip (blockDimX = 64).
    [GpuKernel]
    public static void HybridWarpAndSharedMemoryBlockReduceKernel(int[] input, int[] output, float[] floatOutput)
    {
        int tid = ThreadIndex.X;
        int gid = BlockIndex.X * BlockDimension.X + tid;
        int lane = tid & 31;
        int warp = tid >> 5;
        int n = input.Length;
        uint mask = 0xFFFFFFFFu;

        var warpSums = new SharedMemory<int>(2);

        int v = gid < n ? (input[gid] & 0x3FFF) - 0x1FFF : 0;

        int sum = v;
        int s16 = Warp.ShuffleDownSync(mask, sum, 16);
        sum += s16;
        int s8 = Warp.ShuffleDownSync(mask, sum, 8);
        sum += s8;
        int s4 = Warp.ShuffleDownSync(mask, sum, 4);
        sum += s4;
        int s2 = Warp.ShuffleDownSync(mask, sum, 2);
        sum += s2;
        int s1 = Warp.ShuffleDownSync(mask, sum, 1);
        sum += s1;

        if (lane == 0)
        {
            warpSums[warp] = sum;
        }

        Gpu.SyncThreads();

        int blockTotal = warpSums[0] + warpSums[1];
        int otherWarpSum = warpSums[warp ^ 1];

        float fv = (v * 0.25f) + 1.5f;
        float fDown = Warp.ShuffleDownSync(mask, fv, 3);
        float fUp = Warp.ShuffleUpSync(mask, fv, 2);

        if (gid < n)
        {
            int o = gid * 2;
            output[o + 0] = blockTotal;
            output[o + 1] = otherWarpSum;
            floatOutput[o + 0] = fDown;
            floatOutput[o + 1] = fUp;
        }
    }

    // 5. Reproduction of #1001: a sub-warp conditional shuffle (mask = 0x0000FFFFu) on lanes 0..15
    //    followed by a reconverged full-warp shuffle (mask = 0xFFFFFFFFu) across all 32 lanes.
    [GpuKernel]
    public static void SubWarpConditionalShuffleKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;

        int baseVal = tid < n ? (input[tid] & 0xFF) + 1 : 1;
        int a = baseVal * 10 + lane;
        int b = baseVal * 1000 + lane;

        if (lane < 16)
        {
            uint lowHalfMask = 0x0000FFFFu;
            a = Warp.ShuffleDownSync(lowHalfMask, a, 1, 16);
        }

        uint fullMask = 0xFFFFFFFFu;
        b = Warp.ShuffleXorSync(fullMask, b, 16);

        if (tid < n)
        {
            int o = tid * 2;
            output[o + 0] = a;
            output[o + 1] = b;
        }
    }

    // 6. Reproduction of #543: distinct type instantiations of a generic struct constructor
    //    declaring static SharedMemory<int>(32) must have separate shared-memory regions per instantiation,
    //    as monomorphized C++ constructor functions do on CUDA.
    [GpuKernel]
    public static void GenericStructStaticSharedMemoryKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;

        int v = tid < n ? (input[tid] & 0xFF) + 1 : 1;
        int valInt = v * 10 + lane;
        int valUint = v * 1000 + lane;

        var writeInt = new SharedSlot<int>(true, lane, valInt);
        var writeUint = new SharedSlot<uint>(true, lane, valUint);
        int writeZeros = writeInt.Value + writeUint.Value;

        Gpu.SyncThreads();

        int mirrorLane = 31 - lane;
        var readInt = new SharedSlot<int>(false, mirrorLane, 0);
        var readUint = new SharedSlot<uint>(false, mirrorLane, 0);

        if (tid < n)
        {
            int o = tid * 2;
            output[o + 0] = readInt.Value + writeZeros;
            output[o + 1] = readUint.Value + writeZeros;
        }
    }

    // -------------------------------------------------------------------------
    // #1000: Ref ternary writing a value parameter across a synchronization barrier
    // AstLoweringPipeline.FindWrittenStorageRoot only unwraps IdentifierNameSyntax, ParenthesizedExpressionSyntax,
    // MemberAccessExpressionSyntax, and ElementAccessExpressionSyntax, returning null for ConditionalExpressionSyntax
    // (and RefExpressionSyntax). Consequently, a kernel or device-function value parameter mutated via a ref ternary
    // `((cond ? ref param : ref fallback) = ... / += ... / ++...)` is omitted from LoweredStateMachine.CopiedParameters,
    // so resuming after Gpu.SyncThreads() overwrites the mutated parameter with its original host argument on both
    // the managed and native CPU state-machine paths.
    // -------------------------------------------------------------------------
    [GpuKernel]
    public static void RefTernaryParameterWriteAcrossBarrierReproKernel(
        int[] conds,
        int[] dst,
        int paramAssign,
        int paramCompound,
        int paramInc,
        int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int c = conds[t];
        int localFallback = 10;

        ((c & 1) == 0 ? ref paramAssign : ref localFallback) = 777;
        ((c & 2) == 0 ? ref paramCompound : ref localFallback) += 100;
        ++((c & 4) == 0 ? ref paramInc : ref localFallback);

        Gpu.SyncThreads();

        int col = t * 4;
        dst[col + 0] = paramAssign;
        dst[col + 1] = paramCompound;
        dst[col + 2] = paramInc;
        dst[col + 3] = localFallback;
    }
}
