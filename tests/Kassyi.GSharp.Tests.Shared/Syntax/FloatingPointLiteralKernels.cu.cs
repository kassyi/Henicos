using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class FloatingPointLiteralKernels
{
    [GpuKernel]
    public static void FloatLiteralKernel(float[] values)
    {
        int i = ThreadIndex.X;
        // No decimal point: C++ needs "2.0f"; an uppercase suffix and an exponent must survive as well
        values[i] = values[i] * 2f + 1F + 1e1f;
    }

    [GpuKernel]
    public static void DoubleLiteralKernel(double[] values)
    {
        int i = ThreadIndex.X;
        // C++ has no "d" suffix
        values[i] = values[i] * 2d + 1.5d + 1D;
    }
}
