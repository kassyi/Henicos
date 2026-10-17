// ReSharper disable All

#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorAddScalarKernel(VectorAddSubContext ctx)
    {
        ctx.SignB = ctx.SignA;
        ctx.OpSign = 0u;
        ctx.IsScalar = true;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorSubScalarKernel(VectorAddSubContext ctx)
    {
        ctx.SignB = ctx.SignA;
        ctx.OpSign = 1u;
        ctx.IsScalar = true;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorZipAddKernel(VectorAddSubContext ctx)
    {
        ctx.OpSign = 0u;
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorZipSubKernel(VectorAddSubContext ctx)
    {
        ctx.OpSign = 1u;
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatAddScalarKernel(VectorFloatAddSubContext ctx)
    {
        ctx.ExpB = ctx.ExpA;
        ctx.SignB = ctx.SignA;
        ctx.OpSign = 0u;
        ctx.IsScalar = true;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatSubScalarKernel(VectorFloatAddSubContext ctx)
    {
        ctx.ExpB = ctx.ExpA;
        ctx.SignB = ctx.SignA;
        ctx.OpSign = 1u;
        ctx.IsScalar = true;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatZipAddKernel(VectorFloatAddSubContext ctx)
    {
        ctx.OpSign = 0u;
        ctx.ScalarExp = 0;
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatZipSubKernel(VectorFloatAddSubContext ctx)
    {
        ctx.OpSign = 1u;
        ctx.ScalarExp = 0;
        ctx.ScalarSign = 0u;
        ctx.IsScalar = false;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubParallel(VectorAddSubContext ctx, bool syncThreads = true)
    {
        DeviceVectorAddSubAlignedCore(ctx, syncThreads);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorAddSubAlignedCore(VectorAddSubContext ctx, bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        uint sSign = ctx.IsScalar ? ctx.ScalarSign : ctx.SignB[batchIdx];
        uint effSignB = sSign ^ ctx.OpSign;
        bool differentSigns = (ctx.SignA[batchIdx] != effSignB);

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = differentSigns ? 1u : 0u;
        int baseA = batchIdx * ctx.NumWordsA;
        int baseB = (ctx.IsScalar ? 0 : batchIdx) * ctx.NumWordsB;
        int baseResult = batchIdx * ctx.NumWordsResult;

        for (int offset = 0; offset < ctx.NumWordsResult; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < ctx.NumWordsResult;
            uint valA = valid ? GetVectorShiftedWord(ctx.A, baseA, ctx.NumWordsA, idx, ctx.BitOffsetA) : 0u;
            uint valB = valid ? GetVectorShiftedWord(ctx.B, baseB, ctx.NumWordsB, idx, ctx.BitOffsetB) : 0u;

            if (differentSigns)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
            if (valid)
            {
                ctx.Result[baseResult + idx] = (uint)sum + carryIn;
            }

            Gpu.SyncThreads();
        }

        SharedMemory<uint> needsNegate = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            if (!differentSigns)
            {
                ctx.SignResult[batchIdx] = ctx.SignA[batchIdx];
                needsNegate[0] = 0;
            }
            else
            {
                uint topWord = ctx.Result[baseResult + ctx.NumWordsResult - 1];
                bool isNegTC = (topWord & 0x80000000u) != 0;
                ctx.SignResult[batchIdx] = isNegTC ? effSignB : ctx.SignA[batchIdx];
                needsNegate[0] = isNegTC ? 1u : 0u;
            }
        }

        Gpu.SyncThreads();

        if (needsNegate[0] == 1u)
        {
            blockCarryIn = 1u;
            for (int offset = 0; offset < ctx.NumWordsResult; offset += bDim)
            {
                int idx = offset + tid;
                bool valid = idx < ctx.NumWordsResult;
                uint valA = valid ? ~ctx.Result[baseResult + idx] : 0u;

                uint g = 0u;
                uint p = valA == 0xFFFFFFFFu ? 1u : 0u;

                uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
                if (valid)
                {
                    ctx.Result[baseResult + idx] = valA + carryIn;
                }

                Gpu.SyncThreads();
            }
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatAddSubParallel(VectorFloatAddSubContext ctx, bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int batchIdx = BlockIndex.X;

        int eA = (int)ctx.ExpA[batchIdx];
        int eB = ctx.IsScalar ? ctx.ScalarExp : (int)ctx.ExpB[batchIdx];

        int bitOffsetA = 0;
        int bitOffsetB = 0;
        int outExp = eA;

        if (eA > eB)
        {
            int diff = eA - eB;
            int headroomA = (ctx.NumWordsResult - ctx.NumWordsA) * 32;
            if (headroomA < 0) headroomA = 0;
            if (diff <= headroomA)
            {
                bitOffsetA = diff;
                outExp = eB;
            }
            else
            {
                bitOffsetB = -diff;
                outExp = eA;
            }
        }
        else if (eB > eA)
        {
            int diff = eB - eA;
            int headroomB = (ctx.NumWordsResult - ctx.NumWordsB) * 32;
            if (headroomB < 0) headroomB = 0;
            if (diff <= headroomB)
            {
                bitOffsetB = diff;
                outExp = eA;
            }
            else
            {
                bitOffsetA = -diff;
                outExp = eB;
            }
        }

        if (tid == 0)
        {
            ctx.ExpResult[batchIdx] = (uint)outExp;
        }

        var coreCtx = new VectorAddSubContext
        {
            A = ctx.A,
            B = ctx.B,
            Result = ctx.Result,
            SignA = ctx.SignA,
            SignB = ctx.SignB,
            SignResult = ctx.SignResult,
            NumWordsA = ctx.NumWordsA,
            NumWordsB = ctx.NumWordsB,
            NumWordsResult = ctx.NumWordsResult,
            OpSign = ctx.OpSign,
            ScalarSign = ctx.ScalarSign,
            IsScalar = ctx.IsScalar,
            BitOffsetA = bitOffsetA,
            BitOffsetB = bitOffsetB
        };
        DeviceVectorAddSubAlignedCore(coreCtx, syncThreads);
    }
}
