using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct DigitSeparatorLiteralResults
{
    public int[] Int32Values;
    public uint[] UInt32Values;
    public long[] Int64Values;
    public ulong[] UInt64Values;
    public float[] SingleValues;
    public double[] DoubleValues;
}

public static partial class DigitSeparatorLiteralKernels
{
    [GpuKernel]
    public static void DigitSeparatorKernel(DigitSeparatorLiteralResults results)
    {
        int[] ints = results.Int32Values;
        uint[] uints = results.UInt32Values;
        long[] longs = results.Int64Values;
        ulong[] ulongs = results.UInt64Values;
        float[] floats = results.SingleValues;
        double[] doubles = results.DoubleValues;
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
