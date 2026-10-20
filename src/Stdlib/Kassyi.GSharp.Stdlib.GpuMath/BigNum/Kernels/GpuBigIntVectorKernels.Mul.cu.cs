#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorMulScalarKernel(VectorMulContext ctx)
    {
        if (ThreadIndex.X == 0)
        {
            ctx.Op.Result.Signs[BlockIndex.X] = ctx.Op.Left.Signs[BlockIndex.X] ^ ctx.Op.ScalarSign;
        }

        ctx.Op.IsScalar = true;
        DeviceVectorMulVectorParallel(ctx);
    }

    [GpuKernel]
    public static void VectorZipMulKernel(VectorMulContext ctx)
    {
        if (ThreadIndex.X == 0)
        {
            ctx.Op.Result.Signs[BlockIndex.X] = ctx.Op.Left.Signs[BlockIndex.X] ^ ctx.Op.Right.Signs[BlockIndex.X];
        }

        ctx.Op.IsScalar = false;
        DeviceVectorMulVectorParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatMulScalarKernel(VectorFloatMulContext ctx)
    {
        ctx.FloatOp.Core.IsScalar = true;
        ctx.FloatOp.Core.Right.Signs = ctx.FloatOp.Core.Left.Signs;
        DeviceVectorFloatMulParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatZipMulKernel(VectorFloatMulContext ctx)
    {
        ctx.FloatOp.Core.IsScalar = false;
        ctx.FloatOp.Core.ScalarSign = 0u;
        DeviceVectorFloatMulParallel(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatMulParallel(VectorFloatMulContext ctx)
    {
        int bIdx = BlockIndex.X;
        var core = ctx.FloatOp.Core;
        var exps = ctx.FloatOp.Exponents;
        if (ThreadIndex.X == 0)
        {
            uint sB = core.IsScalar ? core.ScalarSign : core.Right.Signs[bIdx];
            int eB = core.IsScalar ? exps.ScalarExp : (int)exps.ExpB[bIdx];
            core.Result.Signs[bIdx] = core.Left.Signs[bIdx] ^ sB;
            exps.ExpResult[bIdx] = (uint)((int)exps.ExpA[bIdx] + eB);
        }

        var mulCtx = new VectorMulContext
        {
            Op = core
        };
        DeviceVectorMulVectorParallel(mulCtx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorMulVectorParallel(VectorMulContext ctx, bool syncThreads = true)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        int laneId = tid % 32;
        int warpId = tid / 32;

        var op = ctx.Op;
        int offsetA = batchIdx * op.Left.NumWords;
        int offsetB = op.IsScalar ? 0 : batchIdx * op.Right.NumWords;
        int offsetResult = batchIdx * op.Result.NumWords;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        for (int offset = 0; offset < op.Result.NumWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < op.Result.NumWords)
            {
                op.Result.Limbs[offsetResult + idx] = 0u;
            }
        }

        Gpu.SyncThreads();

        for (int i = 0; i < op.Right.NumWords; i++)
        {
            uint bVal = op.Right.Limbs[offsetB + i];
            if (bVal == 0)
            {
                continue;
            }

            uint blockCarryIn = 0u;
            uint blockHIn = 0u;

            for (int offset = 0; offset < op.Left.NumWords; offset += bDim)
            {
                int j = offset + tid;
                bool valid = j < op.Left.NumWords && (i + j < op.Result.NumWords);

                uint aVal = valid ? op.Left.Limbs[offsetA + j] : 0u;
                ulong prod = (ulong)aVal * bVal;
                uint L = (uint)(prod & 0xFFFFFFFFul);
                uint H = (uint)(prod >> 32);

                uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, H, 1);
                if (laneId == 0)
                {
                    prevH = blockHIn;
                }

                ulong sum1 = (ulong)L + prevH;
                uint currentResult = valid ? op.Result.Limbs[offsetResult + i + j] : 0u;
                ulong sum2 = sum1 + currentResult;

                uint finalL = (uint)(sum2 & 0xFFFFFFFFul);
                uint g = (uint)(sum2 >> 32);
                uint p = (uint)sum2 == 0xFFFFFFFFu ? 1u : 0u;

                if (laneId == 31)
                {
                    sharedH[warpId] = H;
                }

                uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim,
                    out blockCarryIn);
                if (valid)
                {
                    op.Result.Limbs[offsetResult + i + j] = finalL + carryIn;
                }

                blockHIn = sharedH[((bDim + 31) / 32) - 1];
                Gpu.SyncThreads();
            }
        }

        if (tid == 0)
        {
            bool isZero = true;
            for (int w = 0; w < op.Result.NumWords; w++)
            {
                if (op.Result.Limbs[offsetResult + w] != 0u)
                {
                    isZero = false;
                    break;
                }
            }

            if (isZero)
            {
                op.Result.Signs[batchIdx] = 0u;
            }
        }
    }
}
