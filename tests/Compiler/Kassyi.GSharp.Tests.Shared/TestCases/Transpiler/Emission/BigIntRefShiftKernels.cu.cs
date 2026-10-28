using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// GpuBigIntRef's << and >> name GpuBigIntKernels.ExecuteShiftLeft / ExecuteShiftRight, each overloaded: the
// (GpuBigIntContext, int, int) overload comes first, but only (ref GpuBigIntContext, uint[], uint[], int) takes the
// operator's types (#478)
public static partial class BigIntRefShiftKernels
{
    [GpuKernel]
    public static void ShiftKernel(uint[] input, uint[] shiftedLeft, uint[] shiftedRight)
    {
        GpuBigIntContext ctx = default;
        GpuBigIntRef x = input;
        GpuBigIntRef left = shiftedLeft;
        GpuBigIntRef right = shiftedRight;
        left = x << 3;
        right = x >> 3;
    }
}
