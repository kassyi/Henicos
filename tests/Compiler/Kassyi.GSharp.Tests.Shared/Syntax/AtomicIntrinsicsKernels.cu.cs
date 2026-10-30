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
    // old[slot * BlockDimension.X + tid]. Thread t adds one unit, or passes t + 1, 16 - t, bit t or t shifted into the top
    // bits, so that the checks of AtomicIntrinsicsScenarios hold for any order of the operations and fail for a wrong one.
    [GpuKernel]
    public static void AtomicOldValueKernel(int[] ints, int[] intOld, uint[] uints, uint[] uintOld, ulong[] ulongs,
        ulong[] ulongOld, float[] floats, float[] floatOld)
    {
        int tid = ThreadIndex.X;
        int n = BlockDimension.X;

        intOld[IntSlot.Add * n + tid] = Gpu.AtomicAdd(ref ints[IntSlot.Add], 1);
        intOld[IntSlot.Sub * n + tid] = Gpu.AtomicSub(ref ints[IntSlot.Sub], 1);
        intOld[IntSlot.Exch * n + tid] = Gpu.AtomicExch(ref ints[IntSlot.Exch], tid + 1);
        intOld[IntSlot.Cas * n + tid] = Gpu.AtomicCAS(ref ints[IntSlot.Cas], 0, tid + 1);
        intOld[IntSlot.Min * n + tid] = Gpu.AtomicMin(ref ints[IntSlot.Min], 16 - tid);
        intOld[IntSlot.And * n + tid] = Gpu.AtomicAnd(ref ints[IntSlot.And], ~(1 << tid));
        intOld[IntSlot.Xor * n + tid] = Gpu.AtomicXor(ref ints[IntSlot.Xor], 1 << tid);
        Gpu.ReductionAdd(ref ints[IntSlot.Reduction], tid + 1);

        uintOld[UintSlot.Add * n + tid] = Gpu.AtomicAdd(ref uints[UintSlot.Add], 1u);
        uintOld[UintSlot.Sub * n + tid] = Gpu.AtomicSub(ref uints[UintSlot.Sub], 1u);
        uintOld[UintSlot.Exch * n + tid] = Gpu.AtomicExch(ref uints[UintSlot.Exch], (uint)tid + 1u);
        uintOld[UintSlot.Cas * n + tid] = Gpu.AtomicCAS(ref uints[UintSlot.Cas], 0u, (uint)tid + 1u);
        uintOld[UintSlot.Min * n + tid] = Gpu.AtomicMin(ref uints[UintSlot.Min], (uint)(31 - tid) << 27);
        uintOld[UintSlot.Max * n + tid] = Gpu.AtomicMax(ref uints[UintSlot.Max], (uint)tid << 27);
        uintOld[UintSlot.And * n + tid] = Gpu.AtomicAnd(ref uints[UintSlot.And], ~(1u << tid));
        uintOld[UintSlot.Or * n + tid] = Gpu.AtomicOr(ref uints[UintSlot.Or], 1u << tid);
        uintOld[UintSlot.Xor * n + tid] = Gpu.AtomicXor(ref uints[UintSlot.Xor], 1u << tid);
        uintOld[UintSlot.Inc * n + tid] = Gpu.AtomicInc(ref uints[UintSlot.Inc], 9u);
        uintOld[UintSlot.Dec * n + tid] = Gpu.AtomicDec(ref uints[UintSlot.Dec], 9u);
        Gpu.ReductionAdd(ref uints[UintSlot.Reduction], (uint)tid + 1u);

        ulongOld[UlongSlot.Add * n + tid] = Gpu.AtomicAdd(ref ulongs[UlongSlot.Add], 1ul);
        ulongOld[UlongSlot.Exch * n + tid] = Gpu.AtomicExch(ref ulongs[UlongSlot.Exch], (ulong)tid + 1ul);
        ulongOld[UlongSlot.Cas * n + tid] = Gpu.AtomicCAS(ref ulongs[UlongSlot.Cas], 0ul, (ulong)tid + 1ul);
        ulongOld[UlongSlot.Min * n + tid] = Gpu.AtomicMin(ref ulongs[UlongSlot.Min], (ulong)(31 - tid) << 59);
        ulongOld[UlongSlot.Max * n + tid] = Gpu.AtomicMax(ref ulongs[UlongSlot.Max], (ulong)tid << 59);
        ulongOld[UlongSlot.And * n + tid] = Gpu.AtomicAnd(ref ulongs[UlongSlot.And], ~(1ul << (tid + 32)));
        ulongOld[UlongSlot.Or * n + tid] = Gpu.AtomicOr(ref ulongs[UlongSlot.Or], 1ul << (tid + 32));
        ulongOld[UlongSlot.Xor * n + tid] = Gpu.AtomicXor(ref ulongs[UlongSlot.Xor], 1ul << (tid + 32));
        Gpu.ReductionAdd(ref ulongs[UlongSlot.Reduction], (ulong)tid + 1ul);

        floatOld[FloatSlot.Add * n + tid] = Gpu.AtomicAdd(ref floats[FloatSlot.Add], 1f);
        floatOld[FloatSlot.Exch * n + tid] = Gpu.AtomicExch(ref floats[FloatSlot.Exch], (float)(tid + 1));
        Gpu.ReductionAdd(ref floats[FloatSlot.Reduction], 0.5f);
    }
}

// The slots of AtomicOldValueKernel, one address per operation, shared by the kernel and the checks
internal static class IntSlot
{
    public const int Add = 0, Sub = 1, Exch = 2, Cas = 3, Min = 4, And = 5, Xor = 6, Reduction = 7, Count = 8;
}

internal static class UintSlot
{
    public const int Add = 0, Sub = 1, Exch = 2, Cas = 3, Min = 4, Max = 5, And = 6, Or = 7, Xor = 8, Inc = 9, Dec = 10,
        Reduction = 11, Count = 12;
}

internal static class UlongSlot
{
    public const int Add = 0, Exch = 1, Cas = 2, Min = 3, Max = 4, And = 5, Or = 6, Xor = 7, Reduction = 8, Count = 9;
}

internal static class FloatSlot
{
    public const int Add = 0, Exch = 1, Reduction = 2, Count = 3;
}
