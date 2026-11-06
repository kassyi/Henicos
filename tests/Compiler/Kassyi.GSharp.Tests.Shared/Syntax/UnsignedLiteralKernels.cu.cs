using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class UnsignedLiteralKernels
{
    [GpuKernel]
    public static void UnsuffixedUIntLiteralKernel(uint[] a, uint[] quotients, int[] comparisons, long[] widened)
    {
        int i = ThreadIndex.X;
        // 3000000000 and 2147483648 are uint in C#; an unsuffixed C++ literal of that value is a signed 64-bit type under
        // LP64 (g++), which would turn the 32-bit unsigned wrap into 64-bit signed arithmetic
        quotients[i] = (a[i] + 3000000000) / 2;
        comparisons[i] = a[i] + 2147483648 > 1 ? 1 : 0;
        long r = a[i] + 2147483648;
        widened[i] = r;
    }
}
