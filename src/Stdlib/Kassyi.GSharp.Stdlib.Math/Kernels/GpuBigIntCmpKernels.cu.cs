// ReSharper disable All
#pragma warning disable IDE0051, IDE0052, IDE0059, CS0219

namespace Kassyi.GSharp.Stdlib.Math.Kernels;

[SuppressMessage("ReSharper", "NotAccessedVariable", Justification = "Variables are accessed within inline PTX assembly blocks")]
[SuppressMessage("ReSharper", "RedundantAssignment", Justification = "Assignments are consumed by inline PTX assembly blocks")]
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
        Device_IsZero(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_IsZero(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            if (ctx.Operands.LeftOperand[i] != 0)
            {
                ctx.Operands.ResultBuffer[0] = 1; // Any non-zero element sets this to 1
            }
        });
    }

    [GpuKernel]
    public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, ResultBuffer = dResult },
            NumberOfWords = numWords
        };
        Device_IsOne(ctx);
    }

    [GpuDeviceFunction]
    public static void Device_IsOne(BigIntKernelContext ctx)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
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
        });
    }

    [GpuKernel]
    public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords)
    {
        var ctx = new BigIntKernelContext
        {
            Operands = new BigIntKernelContext.OperandsContext { LeftOperand = dA, RightOperand = dB },
            NumberOfWords = numWords
        };
        Device_CompareTo(ctx, dResult);
    }

    [GpuDeviceFunction]
    public static void Device_CompareTo(BigIntKernelContext ctx, ulong[] dResult)
    {
        Gpu.GridStrideLoop(ctx.NumberOfWords, i =>
        {
            var wa = ctx.Operands.LeftOperand[i];
            var wb = ctx.Operands.RightOperand[i];
            if (wa != wb)
            {
                // Pack index into upper 32 bits, and comparison result (2 if >, 1 if <) into lower 32 bits
                long diff = (long)wa - (long)wb;
                var signMask = (ulong)(diff >> 63);
                var resultBits = 2ul - (signMask & 1ul);
                var val = ((ulong)i << 32) | resultBits;

                // Emits C++ atomicMax
                Gpu.InlinePtx("atomicMax((unsigned long long*)dResult, val);");
            }
        });
    }
}
