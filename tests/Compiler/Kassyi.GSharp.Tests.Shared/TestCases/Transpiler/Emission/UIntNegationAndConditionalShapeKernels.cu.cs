using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// C# negates a uint as a long (C# spec §12.4.7.2) and converts the branches of a conditional to its type (§12.18); C++
// keeps unsigned int for -u and gives c ? u : n the type unsigned int (#671)
public static partial class UIntNegationAndConditionalShapeKernels
{
    // The operand or the branches are converted; a constant branch is written as the converted value
    [GpuKernel]
    public static void ConvertedKernel(uint[] us, int[] ns, short[] ss, byte[] bs, long[] longs, double[] doubles, short[] shorts)
    {
        int tid = ThreadIndex.X;
        uint u = us[tid];
        int n = ns[tid];
        short s = ss[tid];
        byte b = bs[tid];
        bool c = tid == 0;
        longs[0] = -u;
        longs[1] = -(u + 1u);
        longs[2] = c ? u : n;
        longs[3] = c ? u : -1;
        doubles[0] = c ? u : n;
        shorts[0] = c ? s : b;
    }

    // C++ already gives the C# type: nothing is added
    [GpuKernel]
    public static void UnchangedKernel(uint[] us, int[] ns, short[] ss, byte[] bs, long[] longs, uint[] values, int[] ints)
    {
        uint u = us[0];
        int n = ns[0];
        short s = ss[0];
        byte b1 = bs[0];
        byte b2 = bs[1];
        long l = longs[0];
        bool c = n > 0;
        ints[0] = -n;
        ints[1] = -s;
        ints[2] = -b1;
        values[0] = ~u;
        values[1] = +u;
        values[2] = c ? u : 0;
        values[3] = c ? b1 : b2;
        longs[1] = c ? n : l;
        longs[2] = -5u;
    }
}
