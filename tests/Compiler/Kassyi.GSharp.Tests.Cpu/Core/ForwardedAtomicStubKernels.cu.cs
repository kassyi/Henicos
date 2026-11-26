// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Managed-launch kernels exercising every <c>Gpu.Atomic*</c> and <c>Gpu.ReductionAdd</c> overload that
/// <c>ptx_intrinsics.yml</c> forwards to <c>ManagedAtomics</c>.
/// </summary>
[SkipSnapshotConsistency("Managed forwarding coverage for ptx_intrinsics.yml; verified by CoreIntrinsicsTests")]
public static partial class ForwardedAtomicStubKernels
{
    [GpuKernel]
    public static void IntAtomicsKernel(int op, int[] value, int[] returned)
    {
        switch (op)
        {
            case 0: returned[0] = Gpu.AtomicAdd(ref value[0], 1); break;
            case 1: returned[0] = Gpu.AtomicSub(ref value[0], 1); break;
            case 2: returned[0] = Gpu.AtomicExch(ref value[0], 1); break;
            case 3: returned[0] = Gpu.AtomicMin(ref value[0], 1); break;
            case 4: returned[0] = Gpu.AtomicCAS(ref value[0], 7, 1); break;
            case 5: returned[0] = Gpu.AtomicAnd(ref value[0], 1); break;
            case 6: returned[0] = Gpu.AtomicXor(ref value[0], 1); break;
            case 7: Gpu.ReductionAdd(ref value[0], 1); break;
            default: break;
        }
    }

    [GpuKernel]
    public static void UIntAtomicsKernel(int op, uint[] value, uint[] returned)
    {
        switch (op)
        {
            case 0: returned[0] = Gpu.AtomicAdd(ref value[0], 1u); break;
            case 1: returned[0] = Gpu.AtomicSub(ref value[0], 1u); break;
            case 2: returned[0] = Gpu.AtomicOr(ref value[0], 8u); break;
            case 3: returned[0] = Gpu.AtomicMax(ref value[0], 9u); break;
            case 4: returned[0] = Gpu.AtomicExch(ref value[0], 1u); break;
            case 5: returned[0] = Gpu.AtomicMin(ref value[0], 1u); break;
            case 6: returned[0] = Gpu.AtomicInc(ref value[0], 9u); break;
            case 7: returned[0] = Gpu.AtomicDec(ref value[0], 9u); break;
            case 8: returned[0] = Gpu.AtomicCAS(ref value[0], 7u, 1u); break;
            case 9: returned[0] = Gpu.AtomicAnd(ref value[0], 1u); break;
            case 10: returned[0] = Gpu.AtomicXor(ref value[0], 1u); break;
            case 11: Gpu.ReductionAdd(ref value[0], 1u); break;
            default: break;
        }
    }

    [GpuKernel]
    public static void ULongAtomicsKernel(int op, ulong[] value, ulong[] returned)
    {
        switch (op)
        {
            case 0: returned[0] = Gpu.AtomicAdd(ref value[0], 1ul); break;
            case 1: returned[0] = Gpu.AtomicOr(ref value[0], 8ul); break;
            case 2: returned[0] = Gpu.AtomicMax(ref value[0], 9ul); break;
            case 3: returned[0] = Gpu.AtomicExch(ref value[0], 1ul); break;
            case 4: returned[0] = Gpu.AtomicMin(ref value[0], 1ul); break;
            case 5: returned[0] = Gpu.AtomicCAS(ref value[0], 7ul, 1ul); break;
            case 6: returned[0] = Gpu.AtomicAnd(ref value[0], 1ul); break;
            case 7: returned[0] = Gpu.AtomicXor(ref value[0], 1ul); break;
            case 8: Gpu.ReductionAdd(ref value[0], 1ul); break;
            default: break;
        }
    }

    [GpuKernel]
    public static void FloatAtomicsKernel(int op, float[] value, float[] returned)
    {
        switch (op)
        {
            case 0: returned[0] = Gpu.AtomicAdd(ref value[0], 1f); break;
            case 1: returned[0] = Gpu.AtomicExch(ref value[0], 1f); break;
            case 2: Gpu.ReductionAdd(ref value[0], 1f); break;
            default: break;
        }
    }
}
