using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The samples of sections 1 and 8 of docs/spec/en/04_guides/11_deep_struct_marshaling_guide.md, as written there apart
// from the bodies the guide leaves out; a change to those samples is checked here.
public struct DualNttContext
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] Twiddles;
    public int Length;
}

public struct FermatContext
{
    public uint[] OperandA;
    public uint[] OperandB;
    public uint[] Result;
    public ulong Modulus;
    public DualNttContext Ntt;
}

public struct CryptoContext
{
    public uint[] OperandA;
    public uint[] OperandB;
    public uint[] Result;
    public uint[] Scratch;
    public int Length;
}

public static partial class DeepStructMarshalingGuideKernels
{
    // Section 1: a device function with default parameters, called with the defaults and with a named argument
    [GpuDeviceFunction]
    public static void ExecuteDualNtt(
        DualNttContext ntt,
        int stage = 0,
        bool isInverse = false,
        int unrollFactor = 4)
    {
        int tid = ThreadIndex.X;
        if (tid < ntt.Length)
        {
            ntt.Buffer1[tid] += ntt.Twiddles[tid] + (uint)stage + (uint)unrollFactor;
            if (isInverse)
            {
                ntt.Buffer2[tid] += ntt.Twiddles[tid];
            }
        }
    }

    [GpuKernel]
    public static void FermatMegaKernel(FermatContext ctx)
    {
        int tid = ThreadIndex.X;
        ctx.Result[tid] = ctx.OperandA[tid] + ctx.OperandB[tid];

        ExecuteDualNtt(ctx.Ntt);

        ExecuteDualNtt(ctx.Ntt, isInverse: true);
    }

    // Section 8: a device function on raw pointers, called with the array fields of a context pinned by fixed
    [GpuDeviceFunction]
    public static unsafe void FastMontgomeryMultiply(uint* a, uint* b, uint* result, uint* scratch, int len)
    {
        int tid = ThreadIndex.X;
        if (tid < len)
        {
            scratch[tid] = a[tid] * b[tid];
            result[tid] = scratch[tid] + 1u;
        }
    }

    [GpuKernel]
    public static unsafe void ModernKernel(CryptoContext ctx)
    {
        fixed (uint* a = ctx.OperandA, b = ctx.OperandB, result = ctx.Result, scratch = ctx.Scratch)
        {
            FastMontgomeryMultiply(a, b, result, scratch, ctx.Length);
        }
    }
}
