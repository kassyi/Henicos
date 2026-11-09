// ReSharper disable All

#pragma warning disable IDE0051, IDE0022

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
        var op = ctx.Op;
        int batchIdx = BlockIndex.X;
        uint effSignB = (op.IsScalar ? op.ScalarSign : op.Right.Signs[batchIdx]) ^ ctx.OpSign;
        bool differentSigns = op.Left.Signs[batchIdx] != effSignB;

        SharedMemory<uint> sharedCarry = new SharedMemory<uint>(32);
        SharedMemory<uint> topCarry = new SharedMemory<uint>(1);
        SharedMemory<uint> nonZero = new SharedMemory<uint>(1);
        SharedMemory<uint> needsNegate = new SharedMemory<uint>(1);
        if (ThreadIndex.X == 0)
        {
            nonZero[0] = 0u;
        }

        Gpu.SyncThreads();

        AddAlignedMagnitudes(ctx, differentSigns, sharedCarry, topCarry, nonZero);
        if (ThreadIndex.X == 0)
        {
            needsNegate[0] = WriteAddSubSign(op, batchIdx, differentSigns, effSignB, topCarry[0], nonZero[0]) ? 1u : 0u;
        }

        Gpu.SyncThreads();

        if (needsNegate[0] == 1u)
        {
            NegateElement(op.Result.Limbs, batchIdx * op.Result.NumWords, op.Result.NumWords, sharedCarry);
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    // |A| + |B|, or |A| + ~|B| + 1 when the effective signs differ, of the aligned operands of this block's element
    // into its result words; leaves the carry out of the top word (from its own thread: the block carry of a partial
    // warp is not defined) in topCarry[0] and sets nonZero[0] when a result word is not zero
    [GpuDeviceFunction]
    public static void AddAlignedMagnitudes(VectorAddSubContext ctx, bool differentSigns,
        SharedMemory<uint> sharedCarry, SharedMemory<uint> topCarry, SharedMemory<uint> nonZero)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        int batchIdx = BlockIndex.X;
        var op = ctx.Op;
        int words = op.Result.NumWords;
        int baseA = batchIdx * op.Left.NumWords;
        int baseB = (op.IsScalar ? 0 : batchIdx) * op.Right.NumWords;
        int baseResult = batchIdx * words;

        uint blockCarryIn = differentSigns ? 1u : 0u;
        for (int offset = 0; offset < words; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < words;
            uint valA = valid ? GetVectorShiftedWord(op.Left.Limbs, baseA, op.Left.NumWords, idx, ctx.BitOffsetA) : 0u;
            uint valB = valid ? GetVectorShiftedWord(op.Right.Limbs, baseB, op.Right.NumWords, idx, ctx.BitOffsetB) : 0u;
            if (differentSigns)
            {
                valB = ~valB;
            }

            ulong sum = (ulong)valA + valB;
            uint g = (uint)(sum >> 32);
            uint p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(g, p, blockCarryIn, sharedCarry, tid % 32, tid / 32, bDim,
                out blockCarryIn);
            uint word = (uint)sum + carryIn;
            if (valid)
            {
                op.Result.Limbs[baseResult + idx] = word;
                if (word != 0u)
                {
                    nonZero[0] = 1u;
                }
            }

            if (idx == words - 1)
            {
                topCarry[0] = g | (p & carryIn);
            }

            Gpu.SyncThreads();
        }
    }

    // The sign of element batchIdx, written from thread 0; returns whether the result must be negated. Same signs keep
    // the left sign. Different ones: |A| + ~|B| + 1 carries out exactly when |A| >= |B|; a borrow means |B| is larger,
    // the result holds |A| - |B| modulo the width (to negate) and takes B's effective sign. A difference of zero (equal
    // magnitudes) is +0, as BigInteger has no negative zero.
    [GpuDeviceFunction]
    public static bool WriteAddSubSign(VectorBinaryOperationContext op, int batchIdx, bool differentSigns,
        uint effSignB, uint topCarry, uint nonZero)
    {
        if (!differentSigns)
        {
            op.Result.Signs[batchIdx] = op.Left.Signs[batchIdx];
            return false;
        }

        bool borrowed = topCarry == 0u;
        op.Result.Signs[batchIdx] = borrowed ? effSignB : nonZero == 0u ? 0u : op.Left.Signs[batchIdx];
        return borrowed;
    }

    // ~x + 1 over the words words of one element starting at baseIndex, each thread reading and writing its own words
    [GpuDeviceFunction]
    public static void NegateElement(uint[] limbs, int baseIndex, int words, SharedMemory<uint> sharedCarry)
    {
        int tid = ThreadIndex.X;
        int bDim = BlockDimension.X;
        uint blockCarryIn = 1u;
        for (int offset = 0; offset < words; offset += bDim)
        {
            int idx = offset + tid;
            bool valid = idx < words;
            uint value = valid ? ~limbs[baseIndex + idx] : 0u;
            uint p = value == 0xFFFFFFFFu ? 1u : 0u;

            uint carryIn = PropagateBlockCarry(0u, p, blockCarryIn, sharedCarry, tid % 32, tid / 32, bDim,
                out blockCarryIn);
            if (valid)
            {
                limbs[baseIndex + idx] = value + carryIn;
            }

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

        // Magnitudes of the same effective sign are added and can carry out of the top word; opposite ones are
        // subtracted and cannot, as in DeviceVectorAddSubAlignedCore.
        uint signB = core.IsScalar ? core.ScalarSign : core.Right.Signs[batchIdx];
        int carryBits = core.Left.Signs[batchIdx] == (signB ^ ctx.OpSign) ? 1 : 0;

        // The operand with the larger exponent moves left into the headroom of the result, all of it but the bit that
        // a sum needs for its carry; the other operand moves right for the rest of the difference. A difference that
        // fits that room is exact; a larger one drops only bits below the result's last place.
        if (eA > eB)
        {
            int diff = eA - eB;
            int shift = AlignmentShift(diff, core.Result.NumWords, core.Left.NumWords, carryBits);
            bitOffsetA = shift;
            bitOffsetB = -(diff - shift);
            outExp = eA - shift;
        }
        else if (eB > eA)
        {
            int diff = eB - eA;
            int shift = AlignmentShift(diff, core.Result.NumWords, core.Right.NumWords, carryBits);
            bitOffsetB = shift;
            bitOffsetA = -(diff - shift);
            outExp = eB - shift;
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

    // The left shift of the operand with the larger exponent: the exponent difference, at most the bits of the result
    // above the operand less carryBits kept free for the carry (0 when the result is not wider than the operand).
    [GpuDeviceFunction, GpuInline]
    public static int AlignmentShift(int diff, int resultWords, int operandWords, int carryBits)
    {
        int room = resultWords > operandWords ? (resultWords - operandWords) * 32 - carryBits : 0;
        return diff < room ? diff : room;
    }
}
