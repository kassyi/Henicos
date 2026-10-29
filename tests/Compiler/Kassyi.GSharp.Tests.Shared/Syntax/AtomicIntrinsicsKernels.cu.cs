using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class AtomicIntrinsicsKernels
{
    // Assumes a single block of 32 threads: each thread owns one bit.
    [GpuKernel]
    public static void AtomicArithmeticKernel(int[] intState, uint[] uintState, ulong[] ulongState, float[] floatState)
    {
        int tid = ThreadIndex.X;

        Gpu.AtomicSub(ref intState[0], 1);
        Gpu.AtomicMin(ref intState[1], tid - 5);

        Gpu.AtomicAnd(ref uintState[0], ~(1u << tid));
        Gpu.AtomicXor(ref uintState[1], 1u << tid);
        Gpu.AtomicMin(ref uintState[2], (uint)tid);
        Gpu.AtomicMax(ref uintState[3], tid == 31 ? uint.MaxValue : (uint)tid);

        Gpu.AtomicXor(ref ulongState[0], 1ul << (tid + 32));
        Gpu.AtomicMin(ref ulongState[1], (ulong)tid);
        Gpu.AtomicAnd(ref ulongState[2], ~(1ul << tid));

        Gpu.AtomicAdd(ref floatState[0], 0.5f);
        if (tid == 0)
        {
            Gpu.AtomicExch(ref floatState[1], 3.25f);
        }
    }

    // CUDA wrap-around semantics:
    //   atomicInc: old >= limit ? 0 : old + 1
    //   atomicDec: (old == 0 || old > limit) ? limit : old - 1
    [GpuKernel]
    public static void AtomicIncDecWrapKernel(uint[] state, uint limit)
    {
        Gpu.AtomicInc(ref state[0], limit);
        Gpu.AtomicDec(ref state[1], limit);
    }

    [GpuKernel]
    public static void AtomicFloatAddStressKernel(float[] globalSum)
    {
        Gpu.AtomicAdd(ref globalSum[0], 1.0f);
    }

    // Atomic through a ref parameter: the parameter is a T* in C++, so its address must be forwarded as-is.
    [GpuDeviceFunction]
    public static void AccumulateThroughRef(ref int counter, int value)
    {
        Gpu.AtomicAdd(ref counter, value);
    }

    [GpuKernel]
    public static void AtomicRefParameterKernel(int[] counter)
    {
        AccumulateThroughRef(ref counter[0], ThreadIndex.X + 1);
    }

    // Every overload of every atomic, one slot per operation (#728). Each thread records the old value it got at
    // old[slot * BlockDimension.X + tid]. Thread t adds one unit or passes t + 1, 16 - t, bit t or t shifted into the top
    // bits, so the host can rebuild the order in which the operations ran from the old values alone.
    [GpuKernel]
    public static void AtomicOldValueKernel(int[] ints, int[] intOld, uint[] uints, uint[] uintOld, ulong[] ulongs,
        ulong[] ulongOld, float[] floats, float[] floatOld)
    {
        int tid = ThreadIndex.X;
        int n = BlockDimension.X;

        intOld[0 * n + tid] = Gpu.AtomicAdd(ref ints[0], 1);
        intOld[1 * n + tid] = Gpu.AtomicSub(ref ints[1], 1);
        intOld[2 * n + tid] = Gpu.AtomicExch(ref ints[2], tid + 1);
        intOld[3 * n + tid] = Gpu.AtomicCAS(ref ints[3], 0, tid + 1);
        intOld[4 * n + tid] = Gpu.AtomicMin(ref ints[4], 16 - tid);
        intOld[5 * n + tid] = Gpu.AtomicAnd(ref ints[5], ~(1 << tid));
        intOld[6 * n + tid] = Gpu.AtomicXor(ref ints[6], 1 << tid);
        Gpu.ReductionAdd(ref ints[7], tid + 1);

        uintOld[0 * n + tid] = Gpu.AtomicAdd(ref uints[0], 1u);
        uintOld[1 * n + tid] = Gpu.AtomicSub(ref uints[1], 1u);
        uintOld[2 * n + tid] = Gpu.AtomicExch(ref uints[2], (uint)tid + 1u);
        uintOld[3 * n + tid] = Gpu.AtomicCAS(ref uints[3], 0u, (uint)tid + 1u);
        uintOld[4 * n + tid] = Gpu.AtomicMin(ref uints[4], (uint)(31 - tid) << 27);
        uintOld[5 * n + tid] = Gpu.AtomicMax(ref uints[5], (uint)tid << 27);
        uintOld[6 * n + tid] = Gpu.AtomicAnd(ref uints[6], ~(1u << tid));
        uintOld[7 * n + tid] = Gpu.AtomicOr(ref uints[7], 1u << tid);
        uintOld[8 * n + tid] = Gpu.AtomicXor(ref uints[8], 1u << tid);
        uintOld[9 * n + tid] = Gpu.AtomicInc(ref uints[9], 9u);
        uintOld[10 * n + tid] = Gpu.AtomicDec(ref uints[10], 9u);
        Gpu.ReductionAdd(ref uints[11], (uint)tid + 1u);

        ulongOld[0 * n + tid] = Gpu.AtomicAdd(ref ulongs[0], 1ul);
        ulongOld[1 * n + tid] = Gpu.AtomicExch(ref ulongs[1], (ulong)tid + 1ul);
        ulongOld[2 * n + tid] = Gpu.AtomicCAS(ref ulongs[2], 0ul, (ulong)tid + 1ul);
        ulongOld[3 * n + tid] = Gpu.AtomicMin(ref ulongs[3], (ulong)(31 - tid) << 59);
        ulongOld[4 * n + tid] = Gpu.AtomicMax(ref ulongs[4], (ulong)tid << 59);
        ulongOld[5 * n + tid] = Gpu.AtomicAnd(ref ulongs[5], ~(1ul << (tid + 32)));
        ulongOld[6 * n + tid] = Gpu.AtomicOr(ref ulongs[6], 1ul << (tid + 32));
        ulongOld[7 * n + tid] = Gpu.AtomicXor(ref ulongs[7], 1ul << (tid + 32));
        Gpu.ReductionAdd(ref ulongs[8], (ulong)tid + 1ul);

        floatOld[0 * n + tid] = Gpu.AtomicAdd(ref floats[0], 1f);
        floatOld[1 * n + tid] = Gpu.AtomicExch(ref floats[1], (float)(tid + 1));
        Gpu.ReductionAdd(ref floats[2], 0.5f);
    }
}
