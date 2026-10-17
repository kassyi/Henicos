#pragma warning disable IDE0051, IDE0022, GSH0014

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// [SkipSnapshotConsistency("Tested in separate suites")]
public static partial class GpuBigIntVectorKernels
{
    [GpuKernel]
    public static void VectorMulScalarKernel(VectorMulContext ctx)
    {
        if (ThreadIndex.X == 0)
        {
            ctx.SignResult[BlockIndex.X] = ctx.SignA[BlockIndex.X] ^ ctx.ScalarSign;
        }

        ctx.IsScalar = true;
        DeviceVectorMulVectorParallel(ctx);
    }

    [GpuKernel]
    public static void VectorZipMulKernel(VectorMulContext ctx)
    {
        if (ThreadIndex.X == 0)
        {
            ctx.SignResult[BlockIndex.X] = ctx.SignA[BlockIndex.X] ^ ctx.SignB[BlockIndex.X];
        }

        ctx.IsScalar = false;
        DeviceVectorMulVectorParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatMulScalarKernel(VectorFloatMulContext ctx)
    {
        ctx.IsScalar = true;
        ctx.SignB = ctx.SignA;
        DeviceVectorFloatMulParallel(ctx);
    }

    [GpuKernel]
    public static void VectorFloatZipMulKernel(VectorFloatMulContext ctx)
    {
        ctx.IsScalar = false;
        ctx.ScalarSign = 0u;
        DeviceVectorFloatMulParallel(ctx);
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceVectorFloatMulParallel(VectorFloatMulContext ctx)
    {
        int bIdx = BlockIndex.X;
        if (ThreadIndex.X == 0)
        {
            uint sB = ctx.IsScalar ? ctx.ScalarSign : ctx.SignB[bIdx];
            int eB = ctx.IsScalar ? ctx.ScalarExp : (int)ctx.ExpB[bIdx];
            ctx.SignResult[bIdx] = ctx.SignA[bIdx] ^ sB;
            ctx.ExpResult[bIdx] = (uint)((int)ctx.ExpA[bIdx] + eB);
        }

        var mulCtx = new VectorMulContext
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
            ScalarSign = ctx.ScalarSign,
            IsScalar = ctx.IsScalar
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

        int offsetA = batchIdx * ctx.NumWordsA;
        int offsetB = ctx.IsScalar ? 0 : batchIdx * ctx.NumWordsB;
        int offsetResult = batchIdx * ctx.NumWordsResult;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> sharedH = new SharedMemory<uint>(32);

        for (int offset = 0; offset < ctx.NumWordsResult; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < ctx.NumWordsResult)
            {
                ctx.Result[offsetResult + idx] = 0u;
            }
        }

        Gpu.SyncThreads();

        for (int i = 0; i < ctx.NumWordsB; i++)
        {
            uint bVal = ctx.B[offsetB + i];
            if (bVal == 0)
            {
                continue;
            }

            uint blockCarryIn = 0u;
            uint blockHIn = 0u;

            for (int offset = 0; offset < ctx.NumWordsA; offset += bDim)
            {
                int j = offset + tid;
                bool valid = j < ctx.NumWordsA && (i + j < ctx.NumWordsResult);

                uint aVal = valid ? ctx.A[offsetA + j] : 0u;
                ulong prod = (ulong)aVal * bVal;
                uint L = (uint)(prod & 0xFFFFFFFFul);
                uint H = (uint)(prod >> 32);

                uint prevH = Warp.ShuffleUpSync(0xFFFFFFFFu, H, 1);
                if (laneId == 0)
                {
                    prevH = blockHIn;
                }

                ulong sum1 = (ulong)L + prevH;
                uint currentResult = valid ? ctx.Result[offsetResult + i + j] : 0u;
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
                    ctx.Result[offsetResult + i + j] = finalL + carryIn;
                }

                blockHIn = sharedH[((bDim + 31) / 32) - 1];
                Gpu.SyncThreads();
            }
        }
    }
}
