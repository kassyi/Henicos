using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class DigitSeparatorLiteralKernels
{
    [GpuKernel]
    public static void DigitSeparatorKernel(
        int[] ints,
        uint[] uints,
        long[] longs,
        ulong[] ulongs,
        float[] floats,
        double[] doubles)
    {
        int i = ThreadIndex.X;
        // C++ rejects '_' inside numbers, and "0x'FF" is invalid, so separators must be dropped
        ints[i] = 1_000_000;
        uints[i] = 0x_FFFF_0000u;
        longs[i] = 10_000_000_000L;
        ulongs[i] = 0b_1010_1010UL;
        floats[i] = 1_000f;
        doubles[i] = 1_0.5e1_0;
    }
}
