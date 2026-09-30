using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.FastMathTests;

public static partial class FastMathBoundaryKernels
{
    [GpuKernel]
    public static void Kernel_COR01_FastMath_Exp(float[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = FastMath.Exp(input[tid]);
    }

    [GpuKernel]
    public static void Kernel_COR01_FastMath_Log(float[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = FastMath.Log(input[tid]);
    }

    [GpuKernel]
    public static void Kernel_COR01_FastMath_Sqrt(float[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = FastMath.Sqrt(input[tid]);
    }

    [GpuKernel]
    public static void Kernel_COR01_FastMath_Abs(float[] input, float[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = FastMath.Abs(input[tid]);
    }
}
