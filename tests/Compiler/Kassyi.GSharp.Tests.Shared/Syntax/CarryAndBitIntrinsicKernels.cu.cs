// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Kernels that use the carry, funnel shift and bit reverse intrinsics, which the C++ prelude implements on the CPU.</summary>
public static partial class CarryAndBitIntrinsicKernels
{
    [GpuKernel]
    public static void BitIntrinsicsKernel(uint[] input, uint[] output, int n)
    {
        int tid = ThreadIndex.X;
        uint value = input[tid];
        uint next = input[(tid + 1) % n];
        uint shift = (uint)tid;
        output[tid * 3] = Gpu.BitReverse(value);
        output[tid * 3 + 1] = Gpu.FunnelShiftLeft(value, next, shift);
        output[tid * 3 + 2] = Gpu.FunnelShiftRight(value, next, shift);
    }

    [GpuKernel]
    public static void CarryChainKernel(uint[] a, uint[] b, uint[] sum, ulong[] carryOut, uint[] difference,
        long[] borrowOut)
    {
        int tid = ThreadIndex.X;
        ulong carry = (ulong)(tid & 1);
        sum[tid] = Gpu.AddWithCarry(a[tid], b[tid], ref carry);
        carryOut[tid] = carry;

        long borrow = tid & 1;
        difference[tid] = Gpu.SubWithBorrow(a[tid], b[tid], ref borrow);
        borrowOut[tid] = borrow;
    }
}
