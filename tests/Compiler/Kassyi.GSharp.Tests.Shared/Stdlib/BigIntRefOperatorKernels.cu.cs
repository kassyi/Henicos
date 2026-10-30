using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

// GpuBigIntRef's << and >> name GpuBigIntKernels.ExecuteShiftLeft / ExecuteShiftRight of Kassyi.GSharp.Stdlib.GpuMath,
// each overloaded (#478). The managed fallback expands them into calls of the library's C# methods (#711)
public static partial class BigIntRefOperatorKernels
{
    [GpuKernel]
    public static void ShiftKernel(uint[] input, uint[] shiftedLeft, uint[] shiftedRight, int numberOfWords)
    {
        GpuBigIntContext ctx = default;
        ctx.NumberOfWords = numberOfWords;
        GpuBigIntRef x = input;
        GpuBigIntRef left = shiftedLeft;
        GpuBigIntRef right = shiftedRight;
        left = x << 3;
        right = x >> 3;
    }
}
