// ReSharper disable NotAccessedVariable
// ReSharper disable RedundantAssignment
#pragma warning disable IDE0051, IDE0052, IDE0059, CS0219
using Kassyi.GSharp.Stdlib.Math.Contexts;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

public static class GpuBigIntCmpKernels
{
    [GpuKernel]
    public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_IsZero(ref ctx);
    }

    [GpuDevice]
    public static void Device_IsZero(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= ctx.NumberOfWords)
        {
            return;
        }

        if (ctx.Operands.LeftOperand[i] != 0)
        {
            ctx.Operands.ResultBuffer[0] = 1; // Any non-zero element sets this to 1
        }
    }

    [GpuKernel]
    public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_IsOne(ref ctx);
    }

    [GpuDevice]
    public static void Device_IsOne(ref BigIntKernelContext ctx)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= ctx.NumberOfWords)
        {
            return;
        }

        if (i == 0)
        {
            if (ctx.Operands.LeftOperand[0] != 1)
            {
                ctx.Operands.ResultBuffer[0] = 1;
            }
        }
        else
        {
            if (ctx.Operands.LeftOperand[i] != 0)
            {
                ctx.Operands.ResultBuffer[0] = 1;
            }
        }
    }

    [GpuKernel]
    public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB },
            NumberOfWords = numWords
        };
        Device_CompareTo(ref ctx, dResult);
    }

    [GpuDevice]
    public static void Device_CompareTo(ref BigIntKernelContext ctx, ulong[] dResult)
    {
        var i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (i >= ctx.NumberOfWords)
        {
            return;
        }

        var wa = ctx.Operands.LeftOperand[i];
        var wb = ctx.Operands.RightOperand[i];
        if (wa == wb)
        {
            return;
        }

        // Pack index into upper 32 bits, and comparison result (2 if >, 1 if <) into lower 32 bits
        var val = (ulong)i << 32;
        if (wa > wb)
        {
            val |= 2ul;
        }
        else
        {
            val |= 1ul;
        }

        // Emits C++ atomicMax
        Gpu.InlinePtx("atomicMax((unsigned long long*)dResult, val);");
    }
}
