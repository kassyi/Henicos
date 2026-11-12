#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

// The public device functions of GpuBigIntKernels that take the operands from a GpuBigIntContext (LeftOperand,
// RightOperand, ResultBuffer), called from user kernels; no operator macro of GpuBigIntRef binds these overloads
public static partial class BigIntContextOperationKernels
{
    [GpuKernel]
    public static void ShiftLeftKernel(GpuBigIntContext ctx, int wordShift, int bitShift)
    {
        GpuBigIntKernels.ExecuteShiftLeft(ctx, wordShift, bitShift);
    }

    [GpuKernel]
    public static void ShiftRightKernel(GpuBigIntContext ctx, int wordShift, int bitShift)
    {
        GpuBigIntKernels.ExecuteShiftRight(ctx, wordShift, bitShift);
    }

    [GpuKernel]
    public static void AndKernel(GpuBigIntContext ctx)
    {
        GpuBigIntKernels.ExecuteBitwiseAnd(ref ctx);
    }

    [GpuKernel]
    public static void OrKernel(GpuBigIntContext ctx)
    {
        GpuBigIntKernels.ExecuteBitwiseOr(ref ctx);
    }

    [GpuKernel]
    public static void XorKernel(GpuBigIntContext ctx)
    {
        GpuBigIntKernels.ExecuteBitwiseXor(ref ctx);
    }

    [GpuKernel]
    public static void NotKernel(GpuBigIntContext ctx)
    {
        GpuBigIntKernels.ExecuteBitwiseNot(ref ctx);
    }
}
