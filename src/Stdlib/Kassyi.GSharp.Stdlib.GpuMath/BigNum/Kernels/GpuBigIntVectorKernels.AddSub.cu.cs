// ReSharper disable All

#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorAddScalarKernel(VectorAddSubContext ctx)
    {
        ctx.Op.Right.Signs = ctx.Op.Left.Signs;
        ctx.OpSign = 0u;
        ctx.Op.IsScalar = true;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorSubScalarKernel(VectorAddSubContext ctx)
    {
        ctx.Op.Right.Signs = ctx.Op.Left.Signs;
        ctx.OpSign = 1u;
        ctx.Op.IsScalar = true;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorZipAddKernel(VectorAddSubContext ctx)
    {
        ctx.OpSign = 0u;
        ctx.Op.ScalarSign = 0u;
        ctx.Op.IsScalar = false;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorZipSubKernel(VectorAddSubContext ctx)
    {
        ctx.OpSign = 1u;
        ctx.Op.ScalarSign = 0u;
        ctx.Op.IsScalar = false;
        ctx.BitOffsetA = 0;
        ctx.BitOffsetB = 0;
        DeviceVectorAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatAddScalarKernel(VectorFloatAddSubContext ctx)
    {
        ctx.FloatOp.Exponents.ExpB = ctx.FloatOp.Exponents.ExpA;
        ctx.FloatOp.Core.Right.Signs = ctx.FloatOp.Core.Left.Signs;
        ctx.OpSign = 0u;
        ctx.FloatOp.Core.IsScalar = true;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatSubScalarKernel(VectorFloatAddSubContext ctx)
    {
        ctx.FloatOp.Exponents.ExpB = ctx.FloatOp.Exponents.ExpA;
        ctx.FloatOp.Core.Right.Signs = ctx.FloatOp.Core.Left.Signs;
        ctx.OpSign = 1u;
        ctx.FloatOp.Core.IsScalar = true;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatZipAddKernel(VectorFloatAddSubContext ctx)
    {
        ctx.OpSign = 0u;
        ctx.FloatOp.Exponents.ScalarExp = 0;
        ctx.FloatOp.Core.ScalarSign = 0u;
        ctx.FloatOp.Core.IsScalar = false;
        DeviceVectorFloatAddSubParallel(ctx, true);
    }

    [GpuKernel]
    public static void VectorFloatZipSubKernel(VectorFloatAddSubContext ctx)
    {
        ctx.OpSign = 1u;
        ctx.FloatOp.Exponents.ScalarExp = 0;
        ctx.FloatOp.Core.ScalarSign = 0u;
        ctx.FloatOp.Core.IsScalar = false;
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

        var op = ctx.Op;
        uint sSign = op.IsScalar ? op.ScalarSign : op.Right.Signs[batchIdx];
        uint effSignB = sSign ^ ctx.OpSign;
        bool differentSigns = (op.Left.Signs[batchIdx] != effSignB);

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        uint blockCarryIn = differentSigns ? 1u : 0u;
        int baseA = batchIdx * op.Left.NumWords;
        int baseB = (op.IsScalar ? 0 : batchIdx) * op.Right.NumWords;
        int baseResult = batchIdx * op.Result.NumWords;

        for (int offset = 0; offset < op.Result.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < op.Result.NumWords;
            uint valA = valid ? GetVectorShiftedWord(op.Left.Limbs, baseA, op.Left.NumWords, idx, ctx.BitOffsetA) : 0u;
            uint valB = valid ? GetVectorShiftedWord(op.Right.Limbs, baseB, op.Right.NumWords, idx, ctx.BitOffsetB) : 0u;

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
                op.Result.Limbs[baseResult + idx] = (uint)sum + carryIn;
            }

            Gpu.SyncThreads();
        }

        SharedMemory<uint> needsNegate = new SharedMemory<uint>(1);
        if (tid == 0)
        {
            if (!differentSigns)
            {
                op.Result.Signs[batchIdx] = op.Left.Signs[batchIdx];
                needsNegate[0] = 0;
            }
            else
            {
                uint topWord = op.Result.Limbs[baseResult + op.Result.NumWords - 1];
                bool isNegTC = (topWord & 0x80000000u) != 0;
                op.Result.Signs[batchIdx] = isNegTC ? effSignB : op.Left.Signs[batchIdx];
                needsNegate[0] = isNegTC ? 1u : 0u;
            }
        }

        Gpu.SyncThreads();

        if (needsNegate[0] == 1u)
        {
            blockCarryIn = 1u;
            for (int offset = 0; offset < op.Result.NumWords; offset += bDim)
            {
                int idx = offset + tid;
                bool valid = idx < op.Result.NumWords;
                uint valA = valid ? ~op.Result.Limbs[baseResult + idx] : 0u;

                uint g = 0u;
                uint p = valA == 0xFFFFFFFFu ? 1u : 0u;

                uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim, out blockCarryIn);
                if (valid)
                {
                    op.Result.Limbs[baseResult + idx] = valA + carryIn;
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

        var core = ctx.FloatOp.Core;
        var exps = ctx.FloatOp.Exponents;
        int eA = (int)exps.ExpA[batchIdx];
        int eB = core.IsScalar ? exps.ScalarExp : (int)exps.ExpB[batchIdx];

        int bitOffsetA = 0;
        int bitOffsetB = 0;
        int outExp = eA;

        if (eA > eB)
        {
            int diff = eA - eB;
            int headroomA = (core.Result.NumWords - core.Left.NumWords) * 32;
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
            int headroomB = (core.Result.NumWords - core.Right.NumWords) * 32;
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
            exps.ExpResult[batchIdx] = (uint)outExp;
        }

        var coreCtx = new VectorAddSubContext
        {
            Op = core,
            OpSign = ctx.OpSign,
            BitOffsetA = bitOffsetA,
            BitOffsetB = bitOffsetB
        };
        DeviceVectorAddSubAlignedCore(coreCtx, syncThreads);
    }
}
