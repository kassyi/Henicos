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

    /// <summary>
    /// Multiplies the magnitudes of one element per block (schoolbook, one row per word of the right operand) into
    /// <c>Result</c>.
    /// </summary>
    /// <remarks>
    /// The product is exact when <c>Result.NumWords</c> is at least <c>Left.NumWords + Right.NumWords</c>, as every caller
    /// sizes it; words beyond <c>Result.NumWords</c> are not written. When <c>Left.NumWords</c> is greater than
    /// <c>BlockDimension.X</c>, the carry between chunks comes from <see cref="PropagateBlockCarry"/>, so the block width
    /// must be a multiple of 32 (the callers launch <c>min(1024, words)</c> threads, which is 1024 in that case; #887).
    /// </remarks>
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

        int leftWords = op.Left.NumWords;
        int resultWords = op.Result.NumWords;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);

        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            int idx = offset + tid;
            if (idx < resultWords)
            {
                op.Result.Limbs[offsetResult + idx] = 0u;
            }
        }

        Gpu.SyncThreads();

        // Row i adds a * b_i at word i. Thread j holds a_j * b_i = H_j:L_j; the low halves go to words i + j and the
        // high halves to words i + j + 1 in two passes, so that each word adds two values and a carry (below 2^33) and
        // the carry scan stays one bit per word. Adding both halves in one pass would add three values per word, whose
        // carry can be 2 (#883).
        for (int i = 0; i < op.Right.NumWords; i++)
        {
            uint bVal = op.Right.Limbs[offsetB + i];
            if (bVal == 0)
            {
                continue;
            }

            for (int half = 0; half < 2; half++)
            {
                uint blockCarryIn = 0u;
                for (int offset = 0; offset < leftWords; offset += bDim)
                {
                    int j = offset + tid;
                    int pos = i + j + half;
                    bool valid = j < leftWords && pos < resultWords;

                    ulong prod = valid ? (ulong)op.Left.Limbs[offsetA + j] * bVal : 0ul;
                    uint part = half == 0 ? (uint)(prod & 0xFFFFFFFFul) : (uint)(prod >> 32);
                    ulong sum = (ulong)(valid ? op.Result.Limbs[offsetResult + pos] : 0u) + part;
                    uint g = (uint)(sum >> 32);
                    uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

                    uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, laneId, warpId, bDim,
                        out blockCarryIn);
                    if (valid)
                    {
                        op.Result.Limbs[offsetResult + pos] = (uint)sum + carryIn;

                        // The carry out of the low pass's top word goes to word i + leftWords, which no earlier row
                        // reached (row i - 1 ends at word i - 1 + leftWords), so it is assigned, not added. The top
                        // thread computes it itself instead of reading the block carry-out: that is the carry of thread
                        // bDim - 1, which usually holds no word, and is undefined when the last warp is partial (#887).
                        // The high pass carries nothing out: after row i the partial product is below
                        // 2^(32 * (i + 1 + leftWords)).
                        if (half == 0 && j == leftWords - 1 && pos + 1 < resultWords)
                        {
                            op.Result.Limbs[offsetResult + pos + 1] = g | (p & carryIn);
                        }
                    }

                    Gpu.SyncThreads();
                }
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
