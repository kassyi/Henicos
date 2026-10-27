using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// C# computes uint mixed with int, short or sbyte in long (C# spec §12.4.7.3); C++ would pick unsigned int (#671)
public static partial class MixedSignPromotionShapeKernels
{
    // Each operand is converted to long long; a constant is written as the converted value
    [GpuKernel]
    public static void ConvertedKernel(uint[] us, int[] ns, short[] ss, long[] values, int[] flags)
    {
        int tid = ThreadIndex.X;
        uint u = us[tid];
        int n = ns[tid];
        short s = ss[tid];
        values[0] = u + n;
        values[1] = u * n;
        values[2] = u + -8;
        values[3] = u + s;
        var sum = u + n;
        values[4] = sum;
        flags[0] = u > n ? 1 : 0;
        values[5] = (u + 1u) * n;
        values[6] = u * 2u + n;
        values[7] = n - u;
    }

    // The usual arithmetic conversions of C++ already give the C# type: nothing is added
    [GpuKernel]
    public static void UnchangedKernel(uint[] us, int[] ns, byte[] bs, long[] longs, uint[] values)
    {
        uint u = us[0];
        int n = ns[0];
        byte b1 = bs[0];
        byte b2 = bs[1];
        long l = longs[0];
        values[0] = u - 8;
        uint r = u + 8;
        values[1] = r;
        u += 8;
        values[2] = u;
        values[3] = u << 3;
        values[4] = (uint)(b1 + b2);
        longs[1] = n + l;
        longs[2] = u + l;
        values[5] = u + (uint)n;
        values[6] = u + b1;
    }
}
