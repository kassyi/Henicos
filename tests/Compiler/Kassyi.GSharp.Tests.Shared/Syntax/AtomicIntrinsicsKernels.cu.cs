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
}
